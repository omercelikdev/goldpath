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
        AdminSurfaceGuard.Apply(endpoints, group, prefix, exposeUnsecured);
        group.MapGet("/definitions", ([FromServices] GoldpathBulkAdminService<TContext> admin, CancellationToken ct)
            => admin.GetDefinitionsAsync(ct));

        group.MapPost("/batches/{definition}", async (string definition, string? fileName, string? tenant, HttpContext http, [FromServices] GoldpathBulkAdminService<TContext> admin, CancellationToken ct) =>
        {
            var scope = await AdminTenantScope.ResolveAsync(http, tenant);
            return scope.Refusal ?? Results.Ok(await admin.UploadAsync(definition, http.Request.Body, fileName ?? "upload.csv", scope.Tenant, Actor(http), ct));
        });

        group.MapGet("/batches", async (string? state, string? tenant, int? take, HttpContext http, [FromServices] GoldpathBulkAdminService<TContext> admin, CancellationToken ct) =>
        {
            var scope = await AdminTenantScope.ResolveAsync(http, tenant);
            return scope.Refusal ?? Results.Ok(await admin.GetBatchesAsync(state, scope.Tenant, take ?? 50, ct));
        });

        group.MapGet("/batches/{batchId:guid}", async (Guid batchId, string? tenant, HttpContext http, [FromServices] GoldpathBulkAdminService<TContext> admin, CancellationToken ct) =>
        {
            var scope = await AdminTenantScope.ResolveAsync(http, tenant);
            if (scope.Refusal is not null)
            {
                return scope.Refusal;
            }

            return await admin.GetBatchAsync(batchId, scope.Tenant, ct) is { } batch ? Results.Ok(batch) : Results.NotFound();
        });

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
