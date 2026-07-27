using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Goldpath;

/// <summary>One service the console can drive: the operator's word for it, and its head.</summary>
public sealed record GoldpathConsoleService(string Name, string AdminBaseUrl);

/// <summary>
/// How the console is served. The registry lives HERE rather than in a file next to the
/// dist: an adopter who already declares their services in configuration should not also
/// have to drop JSON into a static folder.
/// </summary>
public sealed class GoldpathConsoleOptions
{
    /// <summary>The cross-service registry. Empty means "this app only" — the common case.</summary>
    public IList<GoldpathConsoleService> Services { get; } = [];

    /// <summary>Adds a service the console should offer (console RFC §3).</summary>
    public GoldpathConsoleOptions AddService(string name, string adminBaseUrl)
    {
        Services.Add(new GoldpathConsoleService(name, adminBaseUrl));
        return this;
    }
}

/// <summary>
/// Serves the Goldpath console from the app's own management head (console RFC D1).
/// <para>
/// The console is a single page that talks to the FROZEN admin contract with the
/// operator's own credentials — it adds no capability the API does not already expose.
/// Its assets are embedded in this package: adopters never run Node, and a generated app
/// stays Node-free by construction.
/// </para>
/// </summary>
public static class GoldpathConsoleEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>
    /// Maps the console under <paramref name="prefix"/>, behind the SAME ops floor as the
    /// admin surfaces (H2). `exposeUnsecured: true` is for hosts that have no auth at all
    /// — the guard logs the choice, exactly as the admin mappers do.
    /// </summary>
    public static IEndpointRouteBuilder MapGoldpathConsole(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/goldpath/console",
        bool exposeUnsecured = false,
        Action<GoldpathConsoleOptions>? configure = null)
    {
        var options = new GoldpathConsoleOptions();
        configure?.Invoke(options);

        var assembly = typeof(GoldpathConsoleEndpoints).Assembly;
        // The resource prefix follows the ROOT NAMESPACE (Goldpath), not the assembly name —
        // `Goldpath.Console.wwwroot` would find nothing and every request would 404.
        var files = new EmbeddedFileProvider(assembly, "Goldpath.wwwroot");
        var group = endpoints.MapGroup(prefix);
        AdminSurfaceGuard.Apply(endpoints, group, prefix, exposeUnsecured);

        // The registry, as the console reads it. Served from CONFIG so the same dist works
        // for every adopter.
        //
        // An app that configures NO service has no registry, and says so with 404 — which
        // the console reads as "this service only", silently and correctly. Answering with
        // an empty LIST instead looked equivalent and was not: the console reads an empty
        // registry as a BROKEN one and warns the operator that a service they configured is
        // missing. Every single-app adopter met that false warning on their first screen
        // (caught by the README screenshot run, because the two halves were each tested
        // alone and the seam between them never was).
        group.MapGet("/console.config.json", () =>
        {
            if (options.Services.Count == 0)
            {
                return Results.NotFound();
            }

            var payload = JsonSerializer.Serialize(new
            {
                services = options.Services.Select(service => new { name = service.Name, adminBaseUrl = service.AdminBaseUrl }),
            });
            return Results.Content(payload, "application/json", Encoding.UTF8);
        });

        group.MapGet("/{**path}", (string? path) => Serve(files, path));

        return endpoints;
    }

    /// <summary>
    /// Resolves one request against the embedded console. Internal rather than private so
    /// the three outcomes an operator can hit — the page, a missing asset, and a package
    /// built without a console — are proven deterministically instead of depending on
    /// whether the machine running the tests happened to build the dist.
    /// </summary>
    internal static IResult Serve(IFileProvider files, string? path)
    {
        var requested = path?.Replace('\\', '/').Trim('/') ?? "";

        // The PAGE is the prefix root, and only the root. The console has no client-side
        // routes yet (sections are state, not URLs), so there is nothing to fall back TO:
        // serving the page at an arbitrary path would answer 200 with a document whose
        // relative asset URLs resolve one directory too deep — a blank screen with a green
        // status code. When real routes arrive, this is the place that must also inject a
        // <base href> (open-threads T9).
        if (requested.Length == 0)
        {
            var page = files.GetFileInfo("index.html");
            return page.Exists
                ? Results.Stream(page.CreateReadStream(), ContentTypeOf("index.html"))
                : Results.Problem(
                    "This build of Goldpath.Console carries no console. The package is built by Goldpath's CI from ui/console; a locally built one needs scripts/build-console.sh first.",
                    statusCode: StatusCodes.Status500InternalServerError);
        }

        var asset = files.GetFileInfo(requested);
        // A missing asset must 404 rather than answer with HTML the browser cannot parse —
        // that is how a console "loads" into a blank screen with nothing in the log.
        return asset.Exists ? Results.Stream(asset.CreateReadStream(), ContentTypeOf(requested)) : Results.NotFound();
    }

    /// <summary>
    /// The framework's own extension→type map (ADR-0003: compose, never rewrite). Anything
    /// it does not know is served as bytes rather than guessed at — a guessed content type
    /// is how a console ends up rendered as text.
    /// </summary>
    private static string ContentTypeOf(string path)
        => ContentTypes.TryGetContentType(path, out var contentType) ? contentType : "application/octet-stream";

}
