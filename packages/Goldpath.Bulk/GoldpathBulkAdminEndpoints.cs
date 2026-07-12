using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Goldpath;

/// <summary>The gate payload: the decision's evidence (note mandatory for reject).</summary>
public sealed record GoldpathBulkDecisionRequest(string? Note);

/// <summary>
/// The bulk admin API (§7.1: the API is the contract; the console and the AI skills script
/// THIS). Mount on the management head, behind the auth floor — gate verbs stamp the
/// authenticated principal into the batch row. Run views live in the JOBS console (a batch
/// executes as a run); this surface owns the intake: upload, report, gate.
/// Uploads are raw octet-stream bodies ON PURPOSE: `curl --data-binary @payments.csv` is
/// the whole client story — no multipart ceremony, no antiforgery coupling.
/// </summary>
public static class GoldpathBulkAdminEndpoints
{
    /// <summary>Maps the bulk admin API under <paramref name="prefix"/>.</summary>
    public static IEndpointRouteBuilder MapGoldpathBulkAdmin<TContext>(this IEndpointRouteBuilder endpoints, string prefix = "/goldpath/admin/bulk", bool exposeUnsecured = false)
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

        group.MapGet("/definitions", ([FromServices] GoldpathBulkAdminService<TContext> admin, CancellationToken ct)
            => admin.GetDefinitionsAsync(ct));

        group.MapPost("/batches/{definition}", async (string definition, string? fileName, string? tenant, HttpContext http, [FromServices] GoldpathBulkAdminService<TContext> admin, CancellationToken ct)
            => Results.Ok(await admin.UploadAsync(definition, http.Request.Body, fileName ?? "upload.csv", tenant, Actor(http), ct)));

        group.MapGet("/batches", (string? state, string? tenant, int? take, [FromServices] GoldpathBulkAdminService<TContext> admin, CancellationToken ct)
            => admin.GetBatchesAsync(state, tenant, take ?? 50, ct));

        group.MapGet("/batches/{batchId:guid}", async (Guid batchId, string? tenant, [FromServices] GoldpathBulkAdminService<TContext> admin, CancellationToken ct)
            => await admin.GetBatchAsync(batchId, tenant, ct) is { } batch ? Results.Ok(batch) : Results.NotFound());

        group.MapGet("/batches/{batchId:guid}/errors", (Guid batchId, int? afterRow, int? take, [FromServices] GoldpathBulkAdminService<TContext> admin, CancellationToken ct)
            => admin.GetErrorsAsync(batchId, afterRow ?? 0, take ?? 200, ct));

        group.MapPost("/batches/{batchId:guid}/approve", async (Guid batchId, GoldpathBulkDecisionRequest? request, HttpContext http, [FromServices] GoldpathBulkAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.ApproveAsync(batchId, Actor(http), request?.Note, ct)));

        group.MapPost("/batches/{batchId:guid}/reject", async (Guid batchId, GoldpathBulkDecisionRequest? request, HttpContext http, [FromServices] GoldpathBulkAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.RejectAsync(batchId, Actor(http), request?.Note, ct)));

        return endpoints;
    }

    private static string Actor(HttpContext http)
        => http.User.Identity?.Name ?? "anonymous";

    private static IResult ToResult(GoldpathAdminResult result)
        => result.Ok ? Results.Ok(result) : Results.BadRequest(result);
}
