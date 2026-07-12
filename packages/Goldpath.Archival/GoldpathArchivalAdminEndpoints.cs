using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Goldpath;

/// <summary>Hold payload — the case reference is mandatory (it justifies the hold later).</summary>
public sealed record GoldpathHoldRequest(string CaseReference);

/// <summary>Erasure payload — the subject key names WHO the request is about.</summary>
public sealed record GoldpathErasureRequest(string SubjectKey, string? Detail);

/// <summary>
/// The archival admin API (§7.1: the API is the contract; the console and the AI skills
/// script THIS). Mount on the management head, behind the auth floor — hold and erasure
/// verbs stamp the authenticated principal into their evidence rows. Run views live in the
/// JOBS console (archive/purge/verify are jobs); this surface owns the lifecycle verbs.
/// </summary>
public static class GoldpathArchivalAdminEndpoints
{
    /// <summary>Maps the archival admin API under <paramref name="prefix"/>.</summary>
    public static IEndpointRouteBuilder MapGoldpathArchivalAdmin<TContext>(this IEndpointRouteBuilder endpoints, string prefix = "/goldpath/admin/archival", bool exposeUnsecured = false)
        where TContext : DbContext
    {
        var group = endpoints.MapGroup(prefix);
        AdminSurfaceGuard.Apply(endpoints, group, prefix, exposeUnsecured);
        group.MapGet("/definitions", ([FromServices] GoldpathArchiveAdminService<TContext> admin, CancellationToken ct)
            => admin.GetDefinitionsAsync(ct));

        group.MapGet("/entries/{definition}/{key}", async (string definition, string key, string? tenant, [FromServices] GoldpathArchiveAdminService<TContext> admin, CancellationToken ct)
            => await admin.RetrieveAsync(definition, key, tenant, ct) is { } entry ? Results.Ok(entry) : Results.NotFound());

        group.MapPost("/entries/{definition}/{key}/hold", async (string definition, string key, GoldpathHoldRequest request, HttpContext http, [FromServices] GoldpathArchiveAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.PlaceHoldAsync(definition, key, request.CaseReference, Actor(http), ct)));

        group.MapPost("/entries/{definition}/{key}/lift-hold", async (string definition, string key, HttpContext http, [FromServices] GoldpathArchiveAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.LiftHoldAsync(definition, key, Actor(http), ct)));

        group.MapGet("/holds", (bool? includeLifted, int? take, [FromServices] GoldpathArchiveAdminService<TContext> admin, CancellationToken ct)
            => admin.GetHoldsAsync(includeLifted ?? false, take ?? 100, ct));

        group.MapPost("/entries/{definition}/{key}/erase", async (string definition, string key, GoldpathErasureRequest request, HttpContext http, [FromServices] GoldpathArchiveAdminService<TContext> admin, CancellationToken ct)
            => ToResult(await admin.EraseAsync(definition, key, request.SubjectKey, Actor(http), request.Detail, ct)));

        group.MapGet("/erasures", (int? take, [FromServices] GoldpathArchiveAdminService<TContext> admin, CancellationToken ct)
            => admin.GetErasuresAsync(take ?? 100, ct));

        group.MapPost("/definitions/{definition}/verify", (string definition, [FromServices] GoldpathArchiveAdminService<TContext> admin, CancellationToken ct)
            => admin.VerifyAsync(definition, ct));

        return endpoints;
    }

    private static string Actor(HttpContext http)
        => http.User.Identity?.Name ?? "anonymous";

    private static IResult ToResult(GoldpathAdminResult result)
        => result.Ok ? Results.Ok(result) : Results.BadRequest(result);
}
