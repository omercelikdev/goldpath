using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>Tenant resolution strategy (RFC D1 — path-prefix is a strategic deferral).</summary>
public enum GoldpathTenantStrategy
{
    /// <summary>The <c>X-Goldpath-Tenant</c> header — the gateway-friendly default.</summary>
    Header,

    /// <summary>The first host label (<c>acme.api.bank.com</c> → <c>acme</c>).</summary>
    Subdomain,
}

/// <summary>Tuning surface — bound from <c>Goldpath:MultiTenancy</c>.</summary>
public sealed class GoldpathMultiTenancyOptions
{
    private static readonly string[] s_defaultExemptPaths = ["/health", "/alive", "/openapi"];

    /// <summary>How the tenant is resolved from the request.</summary>
    public GoldpathTenantStrategy Strategy { get; set; } = GoldpathTenantStrategy.Header;

    /// <summary>Fail-closed (RFC D2): unresolvable tenant → 400. Default on.</summary>
    public bool Strict { get; set; } = true;

    /// <summary>Path prefixes served without a tenant. <see langword="null"/> = the defaults.</summary>
    public string[]? ExemptPaths { get; set; }

    internal string[] EffectiveExemptPaths => ExemptPaths ?? s_defaultExemptPaths;
}

/// <summary>
/// Resolves the tenant once per request into the ambient flow
/// (<see cref="GoldpathAmbientTenant"/>) — every seam that already speaks
/// <see cref="ITenantContext"/> lights up from here.
/// </summary>
public sealed class GoldpathMultiTenancyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly GoldpathMultiTenancyOptions _options;

    /// <summary>Creates the middleware.</summary>
    public GoldpathMultiTenancyMiddleware(RequestDelegate next, GoldpathMultiTenancyOptions options)
    {
        _next = next;
        _options = options;
    }

    /// <summary>Resolves the tenant, or fails closed with 400 in strict mode.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        foreach (var exempt in _options.EffectiveExemptPaths)
        {
            if (context.Request.Path.StartsWithSegments(exempt, StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }
        }

        var raw = _options.Strategy == GoldpathTenantStrategy.Header
            ? context.Request.Headers[GoldpathHeaders.TenantId].FirstOrDefault()
            : SubdomainOf(context.Request.Host.Host);

        if (!TenantId.TryCreate(raw, out var tenant))
        {
            GoldpathMultiTenancyMetrics.Unresolved.Add(1);
            if (_options.Strict)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.1",
                    title = "Tenant could not be resolved.",
                    status = 400,
                    detail = _options.Strategy == GoldpathTenantStrategy.Header
                        ? $"The '{GoldpathHeaders.TenantId}' header is missing or invalid."
                        : "The request host carries no valid tenant subdomain.",
                });
                return;
            }

            await _next(context);
            return;
        }

        var previous = GoldpathAmbientTenant.Current;
        GoldpathAmbientTenant.Current = tenant;
        try
        {
            await _next(context);
        }
        finally
        {
            GoldpathAmbientTenant.Current = previous;
        }
    }

    private static string? SubdomainOf(string host)
    {
        if (IPAddress.TryParse(host, out _))
        {
            return null;
        }

        var labels = host.Split('.');
        return labels.Length >= 3 || (labels.Length == 2 && labels[^1] == "localhost")
            ? labels[0]
            : null;
    }
}

/// <summary>Registration and model wiring for multi-tenancy.</summary>
public static class GoldpathMultiTenancyExtensions
{
    /// <summary>Adds tenant resolution, the ambient tenant context, and the stamp/guard contributor.</summary>
    public static TBuilder AddGoldpathMultiTenancy<TBuilder>(this TBuilder builder, Action<GoldpathMultiTenancyOptions>? configure = null)
        where TBuilder : IHostApplicationBuilder
    {
        var options = new GoldpathMultiTenancyOptions();
        builder.Configuration.GetSection("Goldpath:MultiTenancy").Bind(options);
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        builder.Services.TryAddScoped<ITenantContext, AmbientTenantContext>();
        builder.Services.AddScoped<IEntitySaveContributor, TenantStampContributor>();
        return builder;
    }

    /// <summary>Adds the resolution middleware — place it before anything tenant-dependent.</summary>
    public static IApplicationBuilder UseGoldpathMultiTenancy(this IApplicationBuilder app)
        => app.UseMiddleware<GoldpathMultiTenancyMiddleware>();

    /// <summary>
    /// Wires every <see cref="IMultiTenant"/> entity: <see cref="TenantId"/> column conversion
    /// and the tenant query filter (AND-combined with other Goldpath filters — SoftDelete and
    /// MultiTenancy coexist on one entity). Call from <c>OnModelCreating</c> as
    /// <c>modelBuilder.ApplyGoldpathMultiTenancy(this)</c>; forgetting it is analyzer rule GP0901.
    /// The context parameter is what keeps the filter LIVE: EF only re-evaluates filter
    /// values per query execution when they are rooted at the context instance — closure or
    /// static state gets constant-folded into the cached plan, freezing Bypass()/Use()
    /// at their first-seen values (verified empirically on EF 8 and EF 10).
    /// </summary>
    public static ModelBuilder ApplyGoldpathMultiTenancy(this ModelBuilder modelBuilder, DbContext context)
    {
        var converter = new ValueConverter<TenantId, string>(t => t.Value, v => TenantId.Create(v));
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (!typeof(IMultiTenant).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(IMultiTenant.TenantId))
                .HasConversion(converter)
                .HasMaxLength(TenantId.MaxLength);

            var filter = (LambdaExpression)s_tenantFilterFactory
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(null, [context])!;
            GoldpathQueryFilters.AddFilter(entityType, filter);
        }

        return modelBuilder;
    }

    private static readonly MethodInfo s_tenantFilterFactory = typeof(GoldpathMultiTenancyExtensions)
        .GetMethod(nameof(TenantFilter), BindingFlags.NonPublic | BindingFlags.Static)!;

    // Fail-closed: no ambient tenant → no rows. EF rewrites the captured context to the
    // EXECUTING context and parameterizes the member chain per query execution.
    private static LambdaExpression TenantFilter<TEntity>(DbContext context)
        where TEntity : class, IMultiTenant
    {
        Expression<Func<TEntity, bool>> filter = e =>
            context.GoldpathTenantBypassed()
            || (context.GoldpathHasAmbientTenant() && e.TenantId == context.GoldpathAmbientTenantValue());
        return filter;
    }
}

/// <summary>
/// Filter plumbing: surfaces the ambient flow state THROUGH the context instance so EF's
/// query pipeline re-evaluates it on every execution (context-rooted member chains are
/// parameterized; anything else is constant-folded into the cached plan). Not for
/// application code — read <see cref="ITenantContext"/> instead.
/// </summary>
public static class GoldpathTenantDbContextExtensions
{
    /// <summary>Whether the current flow bypasses the tenant filter.</summary>
    public static bool GoldpathTenantBypassed(this DbContext context) => GoldpathTenant.IsBypassed;

    /// <summary>Whether an ambient tenant is set on the current flow.</summary>
    public static bool GoldpathHasAmbientTenant(this DbContext context) => GoldpathAmbientTenant.Current.HasValue;

    /// <summary>The ambient tenant, or <see langword="default"/> when none is set.</summary>
    public static TenantId GoldpathAmbientTenantValue(this DbContext context) => GoldpathAmbientTenant.Current.GetValueOrDefault();
}
