using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Goldpath;

/// <summary>
/// Admin-contract revision R1, in ONE place (shared-source link, like the auth floor):
/// on a multi-tenant app every admin read/verb is scoped to the ambient tenant; widening
/// past it (an explicit `?tenant=` override or the all-tenants view) demands the
/// <see cref="GoldpathPolicies.OpsAllTenants"/> policy on top of the ops floor, and the
/// crossing is logged with the actor. Single-tenant apps (no <see cref="ITenantContext"/>
/// registered) keep the pre-R1 semantics byte-for-byte.
/// </summary>
internal static class AdminTenantScope
{
    /// <summary>The scope decision: either a refusal result, or the effective tenant filter.</summary>
    internal readonly record struct Resolution(IResult? Refusal, string? Tenant);

    internal static async Task<Resolution> ResolveAsync(HttpContext http, string? requested)
    {
        var tenantContext = http.RequestServices.GetService<ITenantContext>();
        if (tenantContext is null)
        {
            // multiTenancy is off: the request's tenant parameter passes through untouched.
            return new Resolution(null, requested);
        }

        var ambient = tenantContext.Current?.ToString();
        var authorization = http.RequestServices.GetService<IAuthorizationService>();
        var crossTenant = authorization is not null
            && (await authorization.AuthorizeAsync(http.User, GoldpathPolicies.OpsAllTenants)).Succeeded;

        if (crossTenant)
        {
            if (requested is null || requested != ambient)
            {
                http.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Goldpath.AdminSurface")
                    .LogWarning("Cross-tenant admin access by {Actor}: requested tenant {Requested} (ambient {Ambient}).",
                        http.User.Identity?.Name ?? "anonymous", requested ?? "<all>", ambient ?? "<none>");
            }

            return new Resolution(null, requested);
        }

        if (requested is not null && requested != ambient)
        {
            return new Resolution(Results.Json(
                new { ok = false, message = $"tenant '{requested}' is outside your scope — the '{GoldpathPolicies.OpsAllTenants}' policy gates cross-tenant admin access" },
                statusCode: StatusCodes.Status403Forbidden), null);
        }

        if (ambient is null)
        {
            return new Resolution(Results.Json(
                new { ok = false, message = "no ambient tenant on a multi-tenant app — admin access is tenant-scoped by default (R1); send the request through tenant resolution or hold the cross-tenant policy" },
                statusCode: StatusCodes.Status400BadRequest), null);
        }

        return new Resolution(null, ambient);
    }

    /// <summary>
    /// For surfaces whose rows carry no tenant column (campaign): on a multi-tenant app
    /// the whole surface is inherently cross-tenant, so it demands the all-tenants
    /// privilege outright. Returns the refusal, or <see langword="null"/> to proceed.
    /// </summary>
    internal static async Task<IResult?> RequireAllTenantsAsync(HttpContext http)
    {
        var tenantContext = http.RequestServices.GetService<ITenantContext>();
        if (tenantContext is null)
        {
            return null;
        }

        var authorization = http.RequestServices.GetService<IAuthorizationService>();
        var allowed = authorization is not null
            && (await authorization.AuthorizeAsync(http.User, GoldpathPolicies.OpsAllTenants)).Succeeded;
        return allowed
            ? null
            : Results.Json(
                new { ok = false, message = $"this surface has no per-tenant rows, so on a multi-tenant app it is cross-tenant by nature — the '{GoldpathPolicies.OpsAllTenants}' policy is required" },
                statusCode: StatusCodes.Status403Forbidden);
    }
}
