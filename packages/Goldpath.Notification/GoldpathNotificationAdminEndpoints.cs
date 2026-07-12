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

        if (exposeUnsecured)
        {
            // The VISIBLE opt-out (hardening H2): legitimate for an internal service
            // behind mTLS/a gateway — and it should read like the decision it is.
            endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Goldpath.AdminSurface")
                .LogWarning("{Prefix} is mapped WITHOUT the ops policy (exposeUnsecured: true) — acceptable only behind an authenticating boundary.", prefix);
        }
        else
        {
            group.RequireAuthorization(GoldpathPolicies.Ops);
        }

        group.MapGet("/templates", ([FromServices] GoldpathNotificationAdminService<TContext> admin, CancellationToken ct)
            => admin.GetTemplatesAsync(ct));

        group.MapGet("/notifications", (string? state, string? template, string? tenant, int? take, [FromServices] GoldpathNotificationAdminService<TContext> admin, CancellationToken ct)
            => admin.GetNotificationsAsync(state, template, tenant, take ?? 50, ct));

        group.MapGet("/notifications/{id:guid}", async (Guid id, string? tenant, [FromServices] GoldpathNotificationAdminService<TContext> admin, CancellationToken ct)
            => await admin.GetNotificationAsync(id, tenant, ct) is { } info ? Results.Ok(info) : Results.NotFound());

        group.MapGet("/suppressions", (int? take, [FromServices] GoldpathNotificationAdminService<TContext> admin, CancellationToken ct)
            => admin.GetSuppressionsAsync(take ?? 100, ct));

        group.MapGet("/failures", (int? take, [FromServices] GoldpathNotificationAdminService<TContext> admin, CancellationToken ct)
            => admin.GetFailuresAsync(take ?? 100, ct));

        return endpoints;
    }
}
