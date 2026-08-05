using Xunit;

namespace Goldpath.Cli.Tests;

public class ExportComposeTests
{
    private const string AppHost = """
        var builder = DistributedApplication.CreateBuilder(args);

        var database = builder.AddPostgres("dbserver").AddDatabase("ordersdb");
        var messaging = builder.AddRabbitMQ("messaging");
        var cache = builder.AddRedis("redis");

        var api = builder.AddProject<Projects.Shop_Api>("api")
            .WithReference(database).WaitFor(database)
            .WithReference(messaging).WaitFor(messaging)
            .WithReference(cache).WaitFor(cache)
            .WithHttpHealthCheck("/health/ready");

        builder.AddProject<Projects.Shop_Gateway>("gateway")
            .WithReference(api)
            .WithHttpHealthCheck("/health/ready");

        builder.Build().Run();
        """;

    private static string Compose()
        => ExportCommand.WriteCompose(ExportCommand.Parse(AppHost), safe => safe.Replace('_', '.'));

    [Fact]
    public void The_apphost_vocabulary_parses_completely()
    {
        var resources = ExportCommand.Parse(AppHost);
        Assert.Equal(["dbserver", "messaging", "redis", "api", "gateway"], resources.Select(r => r.Name));

        var api = resources.Single(r => r.Name == "api");
        Assert.Equal("project", api.Kind);
        Assert.Equal("Shop_Api", api.ProjectSafe);
        Assert.Equal(["database", "messaging", "cache"], api.References);
        Assert.Equal(["database", "messaging", "cache"], api.WaitsFor);

        Assert.Equal("ordersdb", resources.Single(r => r.Name == "dbserver").DatabaseName);
    }

    [Fact]
    public void Container_references_become_the_connection_strings_aspire_would_inject()
    {
        var compose = Compose();
        Assert.Contains("ConnectionStrings__ordersdb: Host=dbserver;Port=5432;Database=ordersdb;Username=postgres;Password=goldpath-dev", compose, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__messaging: amqp://guest:guest@messaging:5672", compose, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__redis: redis:6379", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_reference_becomes_the_service_discovery_shape_and_a_dependency()
    {
        var compose = Compose();
        // The gateway resolves 'api' exactly the way Aspire's discovery config names it.
        Assert.Contains("services__api__http__0: http://api:8080", compose, StringComparison.Ordinal);
        var gatewaySection = compose[compose.IndexOf("  gateway:", StringComparison.Ordinal)..];
        Assert.Contains("depends_on:", gatewaySection, StringComparison.Ordinal);
        Assert.Contains("      api:", gatewaySection, StringComparison.Ordinal);
    }

    [Fact]
    public void WaitFor_on_a_healthchecked_container_waits_for_HEALTHY()
    {
        var compose = Compose();
        var apiSection = compose[compose.IndexOf("  api:", StringComparison.Ordinal)..compose.IndexOf("  gateway:", StringComparison.Ordinal)];
        Assert.Contains("dbserver:\n        condition: service_healthy", apiSection.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("redis:\n        condition: service_started", apiSection.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_environment_lines_survive_with_compose_naming()
    {
        var resources = ExportCommand.Parse("""
            builder.AddProject<Projects.Shop_Worker>("worker")
                .WithEnvironment("Worker__Interval", "00:00:01")
                .WithHttpHealthCheck("/health/ready");
            builder.Build().Run();
            """);
        var compose = ExportCommand.WriteCompose(resources, _ => "Shop.Worker");
        Assert.Contains("Worker__Interval: \"00:00:01\"", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void The_export_lands_compose_and_dockerfiles_on_a_real_app_layout()
    {
        using var app = new FakeApp();
        // The FakeApp AppHost carries the anchors; give it one real chain to export.
        File.WriteAllText(app.AppHost, AppHost.Replace("Shop_Gateway", "Shop_Api2") + "\n// goldpath:features resources\n// goldpath:features references\n// goldpath:workers\n");
        Directory.CreateDirectory(Path.Combine(app.Root, "src", "Shop.Api2"));

        Assert.Equal(0, CliRunner.Run(["export", "compose", "--path", app.Root], new FakeProcessRunner(), TextWriter.Null, TextWriter.Null));
        Assert.True(File.Exists(Path.Combine(app.Root, "docker-compose.yml")));
        Assert.True(File.Exists(Path.Combine(app.Root, "src", "Shop.Api", "Dockerfile")));
        // Re-run is idempotent: the compose regenerates, the Dockerfile is laid ONCE.
        File.AppendAllText(Path.Combine(app.Root, "src", "Shop.Api", "Dockerfile"), "# my edit\n");
        Assert.Equal(0, CliRunner.Run(["export", "compose", "--path", app.Root], new FakeProcessRunner(), TextWriter.Null, TextWriter.Null));
        Assert.Contains("# my edit", File.ReadAllText(Path.Combine(app.Root, "src", "Shop.Api", "Dockerfile")), StringComparison.Ordinal);
    }
}
