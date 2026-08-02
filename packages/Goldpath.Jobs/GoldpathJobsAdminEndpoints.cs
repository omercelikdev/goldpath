using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Goldpath;

/// <summary>
/// The admin API (§7.1: the API is the contract — the S2b dashboard, the portal and the AI
/// skills all script THIS surface; no screen action exists that curl cannot do). Mount it
/// on the management head; put it behind the auth floor (an ops role) — the endpoints
/// resolve the actor from the authenticated principal for the audit trail.
/// </summary>
public static class GoldpathJobsAdminEndpoints
{
    /// <summary>Maps the jobs admin API under <paramref name="prefix"/> (default /goldpath/admin/jobs).</summary>
    public static IEndpointRouteBuilder MapGoldpathJobsAdmin<TContext>(this IEndpointRouteBuilder endpoints, string prefix = "/goldpath/admin/jobs", bool exposeUnsecured = false)
        where TContext : DbContext
    {
        var group = endpoints.MapGroup(prefix);
        AdminSurfaceGuard.Apply(endpoints, group, prefix, exposeUnsecured);
        group.MapGet("/fleets", ([FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => admin.GetFleetsAsync(ct));

        group.MapGet("/fleets/{fleet}/jobs", (string fleet, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => admin.GetJobsAsync(fleet, ct));

        group.MapGet("/fleets/{fleet}/status", async (string fleet, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => await admin.GetFleetStatusAsync(fleet, ct) is { } status ? Results.Ok(status) : Results.NotFound());

        // R3: ?status= repeats — several states are OR'd; absent means all (additive, R2-compatible).
        group.MapGet("/fleets/{fleet}/runs", (string fleet, string? job, int? take, [FromQuery] string[]? status, DateTimeOffset? from, DateTimeOffset? to, Guid? afterId, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => admin.GetRunsAsync(fleet, job, take ?? 50, ct, status, from, to, afterId));

        group.MapGet("/runs/{runId:guid}", async (Guid runId, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => await admin.GetRunAsync(runId, ct) is { } detail ? Results.Ok(detail) : Results.NotFound());

        group.MapPost("/fleets/{fleet}/jobs/{job}/trigger", async (string fleet, string job, bool? dryRun, HttpContext http, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.TriggerAsync(fleet, job, dryRun ?? false, Actor(http), ct)));

        group.MapPost("/fleets/{fleet}/jobs/{job}/pause", async (string fleet, string job, HttpContext http, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.PauseJobAsync(fleet, job, Actor(http), ct)));

        group.MapPost("/fleets/{fleet}/jobs/{job}/resume", async (string fleet, string job, HttpContext http, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.ResumeJobAsync(fleet, job, Actor(http), ct)));

        group.MapPost("/fleets/{fleet}/jobs/{job}/reschedule", async (string fleet, string job, GoldpathRescheduleRequest request, HttpContext http, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.RescheduleAsync(fleet, job, request.Cron, request.TimeZoneId, Actor(http), ct)));

        group.MapPost("/fleets/{fleet}/jobs/{job}/triggers", async (string fleet, string job, GoldpathAddTriggerRequest request, HttpContext http, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.AddTriggerAsync(fleet, job, request.Name, new GoldpathTriggerSpec(request.Cron, request.TimeZoneId, request.Interval, request.RepeatCount, request.CalendarName, request.Priority ?? 5), Actor(http), ct)));

        group.MapDelete("/fleets/{fleet}/jobs/{job}/triggers/{name}", async (string fleet, string job, string name, HttpContext http, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.RemoveTriggerAsync(fleet, job, name, Actor(http), ct)));

        group.MapPost("/fleets/{fleet}/pause-all", async (string fleet, HttpContext http, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.SetFleetPausedAsync(fleet, paused: true, Actor(http), ct)));

        group.MapPost("/fleets/{fleet}/resume-all", async (string fleet, HttpContext http, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.SetFleetPausedAsync(fleet, paused: false, Actor(http), ct)));

        group.MapPost("/runs/{runId:guid}/rerun", async (Guid runId, HttpContext http, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.RerunAsync(runId, Actor(http), ct)));

        group.MapPost("/runs/{runId:guid}/replay-items", async (Guid runId, HttpContext http, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.ReplayItemsAsync(runId, Actor(http), ct)));

        group.MapGet("/fleets/{fleet}/calendars", (string fleet, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => admin.GetCalendarsAsync(fleet, ct));

        group.MapPut("/fleets/{fleet}/calendars/{name}", async (string fleet, string name, GoldpathCalendarSpec spec, HttpContext http, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.PutCalendarAsync(fleet, name, spec, Actor(http), ct)));

        group.MapDelete("/fleets/{fleet}/calendars/{name}", async (string fleet, string name, HttpContext http, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.DeleteCalendarAsync(fleet, name, Actor(http), ct)));

        group.MapGet("/audit", (int? take, [FromServices] GoldpathJobsAdminService<TContext> admin, CancellationToken ct)
            => admin.GetAuditAsync(take ?? 100, ct));

        return endpoints;
    }

    private static string Actor(HttpContext http)
        => http.User.Identity?.Name ?? "anonymous";

    private static IResult ToResult(GoldpathAdminResult result)
        => result.Ok ? Results.Ok(result) : Results.BadRequest(result);
}

/// <summary>Reschedule payload: the audited runtime schedule override (RFC D7).</summary>
public sealed record GoldpathRescheduleRequest(string Cron, string? TimeZoneId);

/// <summary>
/// Add-trigger payload (contract R2.5). Exactly one of <c>Cron</c> or <c>Interval</c>:
/// a trigger is one kind or the other, and the service refuses both or neither rather
/// than silently picking.
/// </summary>
public sealed record GoldpathAddTriggerRequest(
    string Name,
    string? Cron = null,
    string? TimeZoneId = null,
    TimeSpan? Interval = null,
    int? RepeatCount = null,
    string? CalendarName = null,
    int? Priority = null);
