using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Goldpath.Bulk.Tests;

/// <summary>
/// Admin-contract revision R1 — the tenant scope seam (shared-source, compile-linked here
/// exactly like the modules link it). The four behaviors under proof: single-tenant
/// passthrough, ambient scoping, fail-closed refusals (403 foreign / 400 no-ambient),
/// and the privileged crossing.
/// </summary>
public class AdminTenantScopeTests
{
    private sealed class FakeTenantContext(TenantId? current) : ITenantContext
    {
        public TenantId? Current => current;
    }

    private sealed class FakeAuthorization(bool succeed) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(succeed ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(succeed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }

    private static HttpContext Http(string? ambient, bool? privileged)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        if (ambient is not null || privileged is not null)
        {
            // A multi-tenant app: the MARKER is what says so; the context supplies the value.
            services.AddSingleton<GoldpathMultiTenancyMarker>();
            services.AddSingleton<ITenantContext>(new FakeTenantContext(ambient is null ? null : TenantId.Create(ambient)));
        }

        if (privileged is not null)
        {
            services.AddSingleton<IAuthorizationService>(new FakeAuthorization(privileged.Value));
        }

        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private static int? StatusOf(IResult? result) => (result as IStatusCodeHttpResult)?.StatusCode;

    [Fact]
    public async Task Single_tenant_apps_pass_the_request_through_untouched()
    {
        var scope = await AdminTenantScope.ResolveAsync(Http(ambient: null, privileged: null), "anything");

        Assert.Null(scope.Refusal);
        Assert.Equal("anything", scope.Tenant);
    }

    [Fact]
    public async Task Ambient_tenant_is_the_default_scope()
    {
        var scope = await AdminTenantScope.ResolveAsync(Http("acme", privileged: false), requested: null);

        Assert.Null(scope.Refusal);
        Assert.Equal("acme", scope.Tenant);
    }

    [Fact]
    public async Task A_foreign_tenant_request_without_the_privilege_is_a_403()
    {
        var scope = await AdminTenantScope.ResolveAsync(Http("acme", privileged: false), "rival");

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(scope.Refusal));
    }

    [Fact]
    public async Task No_ambient_tenant_on_a_multitenant_app_is_a_400_never_all()
    {
        var scope = await AdminTenantScope.ResolveAsync(Http(ambient: null, privileged: false), requested: null);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(scope.Refusal));
    }

    [Fact]
    public async Task The_privilege_honors_the_request_including_the_all_view()
    {
        var narrowed = await AdminTenantScope.ResolveAsync(Http("acme", privileged: true), "rival");
        Assert.Null(narrowed.Refusal);
        Assert.Equal("rival", narrowed.Tenant);

        var all = await AdminTenantScope.ResolveAsync(Http("acme", privileged: true), requested: null);
        Assert.Null(all.Refusal);
        Assert.Null(all.Tenant);
    }

    [Fact]
    public async Task Tenantless_surfaces_demand_the_privilege_outright_on_multitenant_apps()
    {
        Assert.Null(await AdminTenantScope.RequireAllTenantsAsync(Http(ambient: null, privileged: null)));
        Assert.Equal(StatusCodes.Status403Forbidden,
            StatusOf(await AdminTenantScope.RequireAllTenantsAsync(Http("acme", privileged: false))));
        Assert.Null(await AdminTenantScope.RequireAllTenantsAsync(Http("acme", privileged: true)));
    }

    /// <summary>
    /// The regression the console gate found: composing a BROKER registers an
    /// <see cref="ITenantContext"/> for message-scoped propagation. If that counted as
    /// "this app is multi-tenant", every admin surface of a single-tenant app would start
    /// refusing — 400 on the tenant-scoped ones, 403 on campaign — the moment messaging
    /// joined the composition.
    /// </summary>
    [Fact]
    public async Task A_single_tenant_app_that_composes_messaging_is_still_single_tenant()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<ITenantContext>(new FakeTenantContext(null));   // messaging's own
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        var scope = await AdminTenantScope.ResolveAsync(http, "anything");
        Assert.Null(scope.Refusal);
        Assert.Equal("anything", scope.Tenant);

        Assert.Null(await AdminTenantScope.RequireAllTenantsAsync(http));
    }

    [Fact]
    public async Task A_marked_multi_tenant_app_still_gates_the_tenant_less_surfaces()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<GoldpathMultiTenancyMarker>();
        services.AddSingleton<ITenantContext>(new FakeTenantContext(TenantId.Create("acme")));
        services.AddSingleton<IAuthorizationService>(new FakeAuthorization(false));
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        Assert.Equal(403, StatusOf(await AdminTenantScope.RequireAllTenantsAsync(http)));
    }
}
