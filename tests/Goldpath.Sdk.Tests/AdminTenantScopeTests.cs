using System.Security.Claims;
using Goldpath;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Goldpath.Sdk.Tests;

/// <summary>
/// Admin-contract R1 in one place: single-tenant apps keep pre-R1 semantics byte for
/// byte, multi-tenant apps scope every admin call to the ambient tenant, and widening
/// past it demands the all-tenants policy — refused as a 403 that names the policy.
/// </summary>
public class AdminTenantScopeTests
{
    private sealed class FixedTenant(string? tenant) : ITenantContext
    {
        public TenantId? Current { get; } = tenant is null ? null : TenantId.Create(tenant);
    }

    private sealed class FixedAuthorization(bool allow) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }

    private static HttpContext Host(bool multiTenant, string? ambient = null, bool? crossTenantAllowed = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        if (multiTenant)
        {
            services.AddSingleton(new GoldpathMultiTenancyMarker());
            services.AddSingleton<ITenantContext>(new FixedTenant(ambient));
        }

        if (crossTenantAllowed is { } allow)
        {
            services.AddSingleton<IAuthorizationService>(new FixedAuthorization(allow));
        }

        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    [Fact]
    public async Task A_single_tenant_app_passes_the_request_through_untouched()
    {
        var resolution = await AdminTenantScope.ResolveAsync(Host(multiTenant: false), "anything");
        Assert.Null(resolution.Refusal);
        Assert.Equal("anything", resolution.Tenant);
    }

    [Fact]
    public async Task The_ambient_tenant_IS_the_scope_when_nothing_wider_is_asked()
    {
        var resolution = await AdminTenantScope.ResolveAsync(
            Host(multiTenant: true, ambient: "acme", crossTenantAllowed: false), requested: null);
        Assert.Null(resolution.Refusal);
        Assert.Equal("acme", resolution.Tenant);
    }

    [Fact]
    public async Task Asking_for_a_foreign_tenant_without_the_policy_is_a_403_naming_it()
    {
        var resolution = await AdminTenantScope.ResolveAsync(
            Host(multiTenant: true, ambient: "acme", crossTenantAllowed: false), requested: "rival");
        Assert.NotNull(resolution.Refusal);
        Assert.Null(resolution.Tenant);
    }

    [Fact]
    public async Task No_ambient_tenant_on_a_multitenant_app_is_a_400_not_a_silent_all()
    {
        var resolution = await AdminTenantScope.ResolveAsync(
            Host(multiTenant: true, ambient: null, crossTenantAllowed: false), requested: null);
        Assert.NotNull(resolution.Refusal);
    }

    [Fact]
    public async Task The_all_tenants_policy_widens_the_scope_to_whatever_was_asked()
    {
        var wide = await AdminTenantScope.ResolveAsync(
            Host(multiTenant: true, ambient: "acme", crossTenantAllowed: true), requested: "rival");
        Assert.Null(wide.Refusal);
        Assert.Equal("rival", wide.Tenant);

        var everything = await AdminTenantScope.ResolveAsync(
            Host(multiTenant: true, ambient: "acme", crossTenantAllowed: true), requested: null);
        Assert.Null(everything.Refusal);
        Assert.Null(everything.Tenant);   // null = the all-tenants view, deliberately unfiltered
    }

    [Fact]
    public async Task A_surface_with_no_tenant_rows_demands_the_privilege_outright()
    {
        Assert.Null(await AdminTenantScope.RequireAllTenantsAsync(Host(multiTenant: false)));
        Assert.Null(await AdminTenantScope.RequireAllTenantsAsync(
            Host(multiTenant: true, ambient: "acme", crossTenantAllowed: true)));
        Assert.NotNull(await AdminTenantScope.RequireAllTenantsAsync(
            Host(multiTenant: true, ambient: "acme", crossTenantAllowed: false)));
    }
}
