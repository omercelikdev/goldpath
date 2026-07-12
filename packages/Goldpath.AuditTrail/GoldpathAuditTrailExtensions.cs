using System.Security.Claims;
using Mediant.Behaviors.Configuration;
using Mediant.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>
/// Registers Ring B audit: entity-level stamping + same-transaction change logging (Data
/// seam contributors) composed with Mediant's command-level audit store — one correlated
/// story across both levels.
/// </summary>
public static class GoldpathAuditTrailExtensions
{
    /// <summary>
    /// Adds both audit levels for <typeparamref name="TContext"/>. Call
    /// <c>modelBuilder.AddGoldpathAuditLog()</c> in the context's <c>OnModelCreating</c>.
    /// Commands opt in with Mediant's <c>[Auditable]</c>; entities with
    /// <see cref="IAuditedEntity"/> (stamps) and <see cref="IAuditLogged"/> (change rows).
    /// </summary>
    public static TBuilder AddGoldpathAuditTrail<TBuilder, TContext>(this TBuilder builder, Action<GoldpathAuditTrailOptions>? configure = null)
        where TBuilder : IHostApplicationBuilder
        where TContext : DbContext
    {
        var options = new GoldpathAuditTrailOptions();
        builder.Configuration.GetSection("Goldpath:AuditTrail").Bind(options);
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        // The "who": HTTP claims by default; message flows get it from their own context later.
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddScoped<IUserContext, HttpClaimsUserContext>();

        // Entity level — Data seam contributors (compile-time composition intact).
        builder.Services.AddScoped<IEntitySaveContributor, AuditStampContributor>();
        builder.Services.AddScoped<IEntitySaveContributor, AuditChangeLogContributor>();

        // Command level — Mediant's [Auditable] with the EF store (composed, not rewritten).
        builder.Services.AddMediantEfCoreAuditStore<TContext>();

        // DataProtection alignment: catalog-declared names flow into Mediant's name-pattern
        // masking so the command-level audit masks the same members the entity level does.
        builder.Services.AddOptions<AuditBehaviorOptions>().Configure<IServiceProvider>((mediantOptions, services) =>
        {
            if (services.GetService<IGoldpathDataProtector>() is { } protector)
            {
                foreach (var name in protector.CatalogedPropertyNames)
                {
                    mediantOptions.SensitivePatterns.Add(name);
                }
            }
        });

        return builder;
    }
}

/// <summary>Resolves the current user from HTTP claims (name identifier, then name).</summary>
public sealed class HttpClaimsUserContext : IUserContext
{
    private readonly IHttpContextAccessor _accessor;

    /// <summary>Creates the context over the HTTP accessor.</summary>
    public HttpClaimsUserContext(IHttpContextAccessor accessor) => _accessor = accessor;

    /// <inheritdoc />
    public string? UserId
        => _accessor.HttpContext?.User is { } user
            ? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.Identity?.Name
            : null;
}
