using Xunit;

namespace Goldpath.Cli.Tests;

public class NewServiceTests
{
    private static int Run(FakeApp app, FakeProcessRunner runner, params string[] args)
        => CliRunner.Run([.. args, "--path", app.Root], runner, TextWriter.Null, TextWriter.Null);

    private static void GiveSmokeAnchor(FakeApp app)
        => File.WriteAllText(Path.Combine(app.Root, "tests", "SmokeTests.cs"), """
            public class SmokeTests
            {
                public async Task Flow()
                {
                    // goldpath:smoke heads — additional heads (goldpath new service|gateway) prove here
                }
            }
            """);

    [Fact]
    public void Service_head_lands_with_its_own_db_manifest_and_deployment_flip()
    {
        using var app = new FakeApp();
        Directory.CreateDirectory(Path.Combine(app.Root, "tests"));
        GiveSmokeAnchor(app);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing"));

        var projectDir = Path.Combine(app.Root, "src", "Shop.BillingService");
        Assert.True(File.Exists(Path.Combine(projectDir, "Shop.BillingService.csproj")));
        Assert.Contains("goldpath:service-head", File.ReadAllText(Path.Combine(projectDir, "Shop.BillingService.csproj")), StringComparison.Ordinal);

        // Its OWN manifest: kind service, namespaced under specs/ (the schema's pattern).
        var manifest = File.ReadAllText(Path.Combine(projectDir, ".goldpath", "manifest.yaml"));
        Assert.Contains("kind: service", manifest, StringComparison.Ordinal);
        Assert.Contains("boundedContext: billing", manifest, StringComparison.Ordinal);
        Assert.Contains("specs/Shop.BillingService.json", manifest, StringComparison.Ordinal);

        // Its OWN database, and the AppHost knows it.
        var appHost = app.Read(app.AppHost);
        Assert.Contains("builder.AddPostgres(\"billing-service-db\")", appHost, StringComparison.Ordinal);
        Assert.Contains("AddProject<Projects.Shop_BillingService>(\"billing-service\")", appHost, StringComparison.Ordinal);

        // The manifest now tells the truth the composition became.
        Assert.Contains("deploymentModel: microservice", app.Read(app.Manifest), StringComparison.Ordinal);

        // The smoke waits on the new head.
        Assert.Contains("CreateHttpClient(\"billing-service\")",
            File.ReadAllText(Path.Combine(app.Root, "tests", "SmokeTests.cs")), StringComparison.Ordinal);

        // The PRIMARY head stays unambiguous for the next verb.
        Assert.Equal(app.ApiProject, AppFiles.Locate(app.Root).ApiProject);
    }

    [Fact]
    public void Gateway_routes_the_api_and_proves_it_through_the_smoke()
    {
        using var app = new FakeApp();
        Directory.CreateDirectory(Path.Combine(app.Root, "tests"));
        GiveSmokeAnchor(app);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "gateway"));

        var projectDir = Path.Combine(app.Root, "src", "Shop.Gateway");
        var settings = File.ReadAllText(Path.Combine(projectDir, "appsettings.json"));
        Assert.Contains("\"Path\": \"/api/{**rest}\"", settings, StringComparison.Ordinal);
        Assert.Contains("https+http://api", settings, StringComparison.Ordinal);

        var appHost = app.Read(app.AppHost);
        Assert.Contains("var api = builder.AddProject", appHost, StringComparison.Ordinal);   // the chain gains its handle
        Assert.Contains(".WithReference(api)", appHost, StringComparison.Ordinal);
        Assert.Contains("modules: [yarpGateway]", app.Read(app.Manifest), StringComparison.Ordinal);

        // The ROUTED probe: green through the gateway is the whole chain answering.
        Assert.Contains("GetAsync(\"/api/health/ready\"",
            File.ReadAllText(Path.Combine(app.Root, "tests", "SmokeTests.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_service_after_the_gateway_registers_its_route_and_reference()
    {
        using var app = new FakeApp();
        Directory.CreateDirectory(Path.Combine(app.Root, "tests"));
        GiveSmokeAnchor(app);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "gateway"));
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing"));

        var settings = File.ReadAllText(Path.Combine(app.Root, "src", "Shop.Gateway", "appsettings.json"));
        Assert.Contains("\"Path\": \"/billing-service/{**rest}\"", settings, StringComparison.Ordinal);
        Assert.Contains(".WithReference(Shop_BillingServiceResource)", app.Read(app.AppHost), StringComparison.Ordinal);
    }

    [Fact]
    public void The_name_survives_any_flag_order()
    {
        // review R3: `--path <dir> Billing` must name Billing, never the path value.
        using var app = new FakeApp();
        Directory.CreateDirectory(Path.Combine(app.Root, "tests"));
        GiveSmokeAnchor(app);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, CliRunner.Run(["new", "service", "--path", app.Root, "Billing"],
            runner, TextWriter.Null, TextWriter.Null));
        Assert.True(Directory.Exists(Path.Combine(app.Root, "src", "Shop.BillingService")));
    }

    [Fact]
    public void A_red_engine_restores_everything_byte_identical()
    {
        using var app = new FakeApp();
        Directory.CreateDirectory(Path.Combine(app.Root, "tests"));
        GiveSmokeAnchor(app);
        var before = new[] { app.Manifest, app.AppHost, app.AppHostProject }
            .ToDictionary(p => p, app.Read, StringComparer.Ordinal);

        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["validate"] = 1;
        Assert.Equal(1, Run(app, runner, "new", "service", "Billing"));

        Assert.False(Directory.Exists(Path.Combine(app.Root, "src", "Shop.BillingService")));
        foreach (var (path, text) in before)
        {
            Assert.Equal(text, app.Read(path));
        }
    }
}
