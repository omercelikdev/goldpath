using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Goldpath;

/// <summary>
/// The notification admin API (§7.1: the API is the contract). READ-ONLY on purpose:
/// requesting belongs to the APP (the notifier), re-sending belongs to the JOBS console —
/// an admin verb that could inject messages would be an evidence hole. Mount on the
/// management head, behind the auth floor. Recipients are MASKED on every surface here.
/// </summary>
public static class GoldpathNotificationAdminEndpoints
{
    /// <summary>Maps the notification admin API under <paramref name="prefix"/>.</summary>
    public static IEndpointRouteBuilder MapGoldpathNotificationAdmin<TContext>(this IEndpointRouteBuilder endpoints, string prefix = "/goldpath/admin/notification", bool exposeUnsecured = false)
        where TContext : DbContext
    {
        var group = endpoints.MapGroup(prefix);
        AdminSurfaceGuard.Apply(endpoints, group, prefix, exposeUnsecured);
        group.MapGet("/templates", ([FromServices] GoldpathNotificationAdminService<TContext> admin, CancellationToken ct)
            => admin.GetTemplatesAsync(ct));

        group.MapGet("/notifications", async (string? state, string? template, string? tenant, int? take, HttpContext http, [FromServices] GoldpathNotificationAdminService<TContext> admin, CancellationToken ct) =>
        {
            var scope = await AdminTenantScope.ResolveAsync(http, tenant);
            return scope.Refusal ?? Results.Ok(await admin.GetNotificationsAsync(state, template, scope.Tenant, take ?? 50, ct));
        });

        group.MapGet("/notifications/{id:guid}", async (Guid id, string? tenant, HttpContext http, [FromServices] GoldpathNotificationAdminService<TContext> admin, CancellationToken ct) =>
        {
            var scope = await AdminTenantScope.ResolveAsync(http, tenant);
            if (scope.Refusal is not null)
            {
                return scope.Refusal;
            }

            return await admin.GetNotificationAsync(id, scope.Tenant, ct) is { } info ? Results.Ok(info) : Results.NotFound();
        });

        group.MapGet("/suppressions", async (int? take, HttpContext http, [FromServices] GoldpathNotificationAdminService<TContext> admin, CancellationToken ct) =>
        {
            var scope = await AdminTenantScope.ResolveAsync(http, null);
            return scope.Refusal ?? Results.Ok(await admin.GetSuppressionsAsync(scope.Tenant, take ?? 100, ct));
        });

        group.MapGet("/failures", async (int? take, HttpContext http, [FromServices] GoldpathNotificationAdminService<TContext> admin, CancellationToken ct) =>
        {
            var scope = await AdminTenantScope.ResolveAsync(http, null);
            return scope.Refusal ?? Results.Ok(await admin.GetFailuresAsync(scope.Tenant, take ?? 100, ct));
        });

        return endpoints;
    }
}
