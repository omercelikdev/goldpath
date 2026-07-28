using System.Net;
using System.Net.Http.Json;
using Goldpath;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Goldpath.Console.Tests;

/// <summary>
/// What the package OWNS: the routes, the registry it serves from configuration, the ops
/// floor, and the difference between "this is the single page" and "this asset is missing".
/// Whether the embedded console itself renders is proven by the console smoke, which
/// builds the real dist and drives it in a browser — a unit test asserting bytes it also
/// wrote would prove nothing.
/// </summary>
public class MapGoldpathConsoleTests
{
    private static async Task<HttpClient> HostAsync(Action<GoldpathConsoleOptions>? configure = null, bool secured = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        if (secured)
        {
            // The REAL auth floor, composed the way an adopter composes it (ADR-0003):
            // strategy None wires the deny-only scheme and the ops policies, so a guarded
            // route answers an honest 401 rather than failing at request time.
            builder.AddGoldpathAuth(auth => auth.Strategy = GoldpathAuthStrategy.None);
        }

        var app = builder.Build();
        if (secured)
        {
            app.UseGoldpathAuth();
        }

        app.MapGoldpathConsole(exposeUnsecured: !secured, configure: configure);
        await app.StartAsync();
        return app.GetTestClient();
    }

    [Fact]
    public async Task The_registry_is_served_from_configuration_not_from_a_file_beside_the_dist()
    {
        var client = await HostAsync(console => console
            .AddService("payments", "https://payments.internal")
            .AddService("claims", "https://claims.internal"));

        // `GetFromJsonAsync` reads with JsonSerializerDefaults.Web, which is
        // case-INSENSITIVE — the camelCase the endpoint emits binds to these PascalCase
        // records without options (checked after review R5 asked whether it really does).
        var registry = await client.GetFromJsonAsync<Registry>("/goldpath/console/console.config.json");

        Assert.NotNull(registry);
        Assert.Collection(
            registry!.Services,
            first => Assert.Equal(("payments", "https://payments.internal"), (first.Name, first.AdminBaseUrl)),
            second => Assert.Equal(("claims", "https://claims.internal"), (second.Name, second.AdminBaseUrl)));
    }

    [Fact]
    public async Task An_app_that_configures_no_service_has_NO_registry_and_answers_404()
    {
        var client = await HostAsync();

        var response = await client.GetAsync("/goldpath/console/console.config.json");

        // 404 is the one answer the console reads as "this service only" in SILENCE. An
        // empty list is not the same thing and must never come back here: the console's
        // reader (ui/console/src/registry.ts) treats an empty registry as a BROKEN one and
        // warns that a configured service went missing — which is true of a mangled config
        // file and false of an app that simply has one service. The seam is proven for real
        // against a running app in ui/console/e2e/served.spec.ts.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_ASSET_is_a_404_never_the_page_itself()
    {
        var client = await HostAsync();

        var response = await client.GetAsync("/goldpath/console/assets/never-built.css");

        // Answering a stylesheet request with HTML is how a console "loads" and then shows
        // an empty screen with no error anywhere.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void A_package_built_without_its_console_says_SO_instead_of_serving_nothing()
    {
        // Deterministic: an EMPTY provider is a package whose dist was never laid.
        var result = GoldpathConsoleEndpoints.Serve(new NullFileProvider(), path: null);

        var problem = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);
    }

    [Fact]
    public void An_unknown_path_is_404_while_the_console_has_no_client_side_routes()
    {
        var files = new FakeConsole(("index.html", "<!doctype html>"));

        var result = GoldpathConsoleEndpoints.Serve(files, "runs/it-cluster");

        // Answering 200 with the page would look right and BE wrong: its relative asset
        // URLs would resolve a directory too deep and the operator would get a blank
        // screen with a green status code. When the console gains real routes, this test
        // changes together with the <base href> the server must then inject.
        Assert.Equal(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public void The_prefix_root_is_the_page_with_or_without_a_trailing_slash()
    {
        var files = new FakeConsole(("index.html", "<!doctype html>"));

        foreach (var path in new string?[] { null, "", "/" })
        {
            Assert.IsNotAssignableFrom<IStatusCodeHttpResult>(GoldpathConsoleEndpoints.Serve(files, path));
        }
    }

    [Fact]
    public void A_missing_ASSET_is_never_answered_with_the_page()
    {
        var files = new FakeConsole(("index.html", "<!doctype html>"));

        var result = GoldpathConsoleEndpoints.Serve(files, "assets/gone.css");

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task The_console_sits_behind_the_SAME_ops_floor_as_the_admin_surfaces()
    {
        var client = await HostAsync(secured: true);

        var response = await client.GetAsync("/goldpath/console/");

        // No principal, no console: it drives the admin surfaces, so it inherits their floor.
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected the ops floor to refuse, got {(int)response.StatusCode}");
    }

    /// <summary>An embedded console, faked: names and bytes, nothing else.</summary>
    private sealed class FakeConsole(params (string Path, string Content)[] files) : IFileProvider
    {
        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

        public IFileInfo GetFileInfo(string subpath)
            => files.FirstOrDefault(file => file.Path == subpath) is { Path: not null } found
                ? new FakeFile(found.Path, found.Content)
                : new NotFoundFileInfo(subpath);

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }

    private sealed class FakeFile(string name, string content) : IFileInfo
    {
        public bool Exists => true;
        public long Length => Encoding.UTF8.GetByteCount(content);
        public string? PhysicalPath => null;
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => new MemoryStream(Encoding.UTF8.GetBytes(content));
    }

    private sealed record Registry(List<Service> Services);

    private sealed record Service(string Name, string AdminBaseUrl);
}
