using System.Text.Json;
using Xunit;

namespace Goldpath.Cli.Tests;

/// <summary>
/// Mutation-killing companions to <see cref="NewServiceTests"/>: every generated file, every
/// engine call, every refusal is pinned byte-for-byte, so a flipped branch or a blanked
/// literal in <c>NewServiceCommand</c> cannot hide behind a loose <c>Contains</c>.
/// </summary>
public class NewServiceMutationTests
{
    private const string SmokeAnchorLine = "        // goldpath:smoke heads — additional heads (goldpath new service|gateway) prove here";

    private sealed record Outcome(int ExitCode, string Output, string Error);

    private static Outcome Run(FakeApp app, FakeProcessRunner runner, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = CliRunner.Run([.. args, "--path", app.Root], runner, output, error);
        return new Outcome(exit, output.ToString(), error.ToString());
    }

    private static string SmokePath(FakeApp app) => Path.Combine(app.Root, "tests", "SmokeTests.cs");

    private static void GiveSmokeAnchor(FakeApp app)
    {
        Directory.CreateDirectory(Path.Combine(app.Root, "tests"));
        File.WriteAllText(SmokePath(app),
            "public class SmokeTests\n{\n    public async Task Flow()\n    {\n" + SmokeAnchorLine + "\n    }\n}\n");
    }

    private static string Src(FakeApp app, params string[] parts)
        => Path.Combine([app.Root, "src", .. parts]);

    // ── service: generated files, pinned exactly ─────────────────────────────────────

    [Fact]
    public void Service_csproj_is_exact_for_postgres()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);

        Assert.Equal("""
            <Project Sdk="Microsoft.NET.Sdk.Web">

              <!-- goldpath:service-head — an additional head; the PRIMARY head keeps the anchors. -->
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OpenApiDocumentsDirectory>$(MSBuildProjectDirectory)/openapi</OpenApiDocumentsDirectory>
                <OpenApiGenerateDocuments>true</OpenApiGenerateDocuments>
                <OpenApiGenerateDocumentsOnBuild>true</OpenApiGenerateDocumentsOnBuild>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Goldpath.ApiDefaults" />
                <PackageReference Include="Goldpath.ServiceDefaults" />
                <PackageReference Include="Goldpath.Data" />
                <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
                <PackageReference Include="Microsoft.Extensions.ApiDescription.Server" PrivateAssets="all" />
                <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
              </ItemGroup>

            </Project>
            """, File.ReadAllText(Src(app, "Shop.BillingService", "Shop.BillingService.csproj")));
    }

    [Fact]
    public void Service_program_is_exact_for_postgres()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);

        Assert.Equal("""
            using Goldpath;
            using Microsoft.EntityFrameworkCore;
            using Shop.BillingService;

            // A service head (microservice layout): its OWN database, its OWN manifest — the unit
            // Goldpath binds to is the manifest, not the repo (foundation §10). Features compose on
            // the PRIMARY head today; per-service features arrive with the products pilot.
            var builder = WebApplication.CreateBuilder(args);

            builder.AddGoldpathServiceDefaults();
            builder.AddGoldpathApiDefaults();

            // Design time and docgen tolerate a missing connection; nothing connects until used.
            var connection = builder.Configuration.GetConnectionString("billingservicedb");
            builder.AddGoldpathData<WebApplicationBuilder, BillingDb>(options =>
            {
                if (connection is not null)
                {
                    options.UseNpgsql(connection);
                }
                else
                {
                    options.UseNpgsql();
                }
            });

            var app = builder.Build();

            app.MapGoldpathDefaultEndpoints();
            app.MapGoldpathApi();
            app.MapGet("/api/v1/ping", () => new { service = "billing-service", status = "alive" });

            app.Run();
            """, File.ReadAllText(Src(app, "Shop.BillingService", "Program.cs")));
    }

    [Fact]
    public void Service_dbcontext_file_is_exact()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);

        // ServiceDbClass: the LAST dotted segment with Service → Db — file AND class name.
        Assert.Equal("""
            using Goldpath;
            using Microsoft.EntityFrameworkCore;

            namespace Shop.BillingService;

            /// <summary>
            /// This service's OWN schema (db-per-service): starts empty on purpose — entities arrive
            /// with the service's features, migrations with `goldpath db add`.
            /// </summary>
            public class BillingDb(DbContextOptions<BillingDb> options) : DbContext(options)
            {
                /// <inheritdoc />
                protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                    => configurationBuilder.ApplyGoldpathConventions();
            }
            """, File.ReadAllText(Src(app, "Shop.BillingService", "BillingDb.cs")));
    }

    [Fact]
    public void Service_launch_settings_and_manifest_are_exact()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);

        // 5300 + |Σ c*31| % 200 for "Shop.BillingService" — a literal, so the arithmetic cannot drift.
        Assert.Equal("""
            {
              "profiles": {
                "Shop.BillingService": {
                  "commandName": "Project",
                  "applicationUrl": "http://localhost:5442"
                }
              }
            }
            """, File.ReadAllText(Src(app, "Shop.BillingService", "Properties", "launchSettings.json")));

        Assert.Equal("""
            schemaVersion: 1
            kind: service
            name: Shop.BillingService
            description: billing-service — a service head with its own database (db-per-service)
            owner: platform-team
            boundedContext: billing
            specs:
              openapi:
                - specs/Shop.BillingService.json
            """, File.ReadAllText(Src(app, "Shop.BillingService", ".goldpath", "manifest.yaml")));
    }

    [Fact]
    public void Sqlserver_app_gets_the_sqlserver_package_provider_and_server()
    {
        using var app = new FakeApp(sqlServer: true);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);

        var csproj = File.ReadAllText(Src(app, "Shop.BillingService", "Shop.BillingService.csproj"));
        Assert.Contains("    <PackageReference Include=\"Microsoft.EntityFrameworkCore.SqlServer\" />\n", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", csproj, StringComparison.Ordinal);

        var program = File.ReadAllText(Src(app, "Shop.BillingService", "Program.cs"));
        Assert.Contains("        options.UseSqlServer(connection);\n", program, StringComparison.Ordinal);
        Assert.Contains("        options.UseSqlServer();\n", program, StringComparison.Ordinal);
        Assert.DoesNotContain("UseNpgsql", program, StringComparison.Ordinal);

        Assert.Contains("var Shop_BillingServiceDb = builder.AddSqlServer(\"billing-service-db\").AddDatabase(\"billingservicedb\");\n",
            app.Read(app.AppHost), StringComparison.Ordinal);
        Assert.DoesNotContain("AddPostgres(\"billing-service-db\")", app.Read(app.AppHost), StringComparison.Ordinal);
    }

    [Fact]
    public void Service_wires_the_apphost_and_its_csproj_exactly()
    {
        using var app = new FakeApp();
        var appHostBefore = app.Read(app.AppHost);
        var projectBefore = app.Read(app.AppHostProject);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);

        // Wiring lands right after the workers anchor; the split leaves the trailing blank line.
        const string anchor = "// goldpath:workers — additional worker projects wire here (goldpath add worker)";
        var expectedAppHost = appHostBefore.Replace(anchor, anchor + "\n" + """
            var Shop_BillingServiceDb = builder.AddPostgres("billing-service-db").AddDatabase("billingservicedb");
            var Shop_BillingServiceResource = builder.AddProject<Projects.Shop_BillingService>("billing-service")
                .WithReference(Shop_BillingServiceDb).WaitFor(Shop_BillingServiceDb)
                .WithHttpHealthCheck("/health/ready");

            """, StringComparison.Ordinal);
        Assert.Equal(expectedAppHost, app.Read(app.AppHost));

        const string referenceAnchor = "    <!-- goldpath:workers references — worker projects chain here (goldpath add worker) -->";
        Assert.Equal(
            projectBefore.Replace(referenceAnchor,
                referenceAnchor + "\n    <ProjectReference Include=\"../Shop.BillingService/Shop.BillingService.csproj\" />",
                StringComparison.Ordinal),
            app.Read(app.AppHostProject));
    }

    [Fact]
    public void Service_appends_the_exact_smoke_block()
    {
        using var app = new FakeApp();
        GiveSmokeAnchor(app);
        var before = File.ReadAllText(SmokePath(app));
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);

        Assert.Equal(before.Replace(SmokeAnchorLine, SmokeAnchorLine + "\n" + """
                    var Shop_BillingServiceClient = app.CreateHttpClient("billing-service");
                    await WaitUntilAsync(async () =>
                        (await Shop_BillingServiceClient.GetAsync("/health/ready", timeout.Token)).IsSuccessStatusCode, timeout.Token);
            """, StringComparison.Ordinal), File.ReadAllText(SmokePath(app)));
    }

    [Fact]
    public void Service_prints_the_exact_three_lines()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        var result = Run(app, runner, "new", "service", "Billing");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "── goldpath new service: Shop.BillingService (billing-service) — its OWN database, its OWN manifest (kind: service)\n"
            + "   next: build once, then `goldpath db init` commits its first contract to specs/Shop.BillingService.json and generates its Initial migration once it has entities;\n"
            + "   features still compose on the PRIMARY head — per-service features arrive with the products pilot (platform RFC).\n",
            result.Output.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void Service_calls_sln_add_then_validate_validate_manifest_drift_in_order()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);

        var sln = Path.Combine(app.Root, "Shop.sln");
        var csproj = Src(app, "Shop.BillingService", "Shop.BillingService.csproj");
        Assert.Equal(4, runner.Calls.Count);

        Assert.Equal("dotnet", runner.Calls[0].FileName);
        Assert.Equal(["sln", sln, "add", csproj], runner.Calls[0].Arguments);
        Assert.Equal(app.Root, runner.Calls[0].WorkingDirectory);

        Assert.Equal("validate", runner.Calls[1].Arguments[0]);
        Assert.Equal(Path.Combine(".goldpath", "manifest.yaml"), runner.Calls[1].Arguments[1]);

        // The NEW manifest is validated at its own relative path — src/<project>/.goldpath/manifest.yaml.
        Assert.Equal("validate", runner.Calls[2].Arguments[0]);
        Assert.Equal(Path.Combine("src", "Shop.BillingService", ".goldpath", "manifest.yaml"), runner.Calls[2].Arguments[1]);

        Assert.Equal("drift", runner.Calls[3].Arguments[0]);
    }

    [Fact]
    public void Service_manifest_without_architecture_block_gains_one()
    {
        using var app = new FakeApp();
        var before = app.Read(app.Manifest);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);
        Assert.Equal(before.TrimEnd('\n') + "\narchitecture:\n  deploymentModel: microservice\n", app.Read(app.Manifest));
    }

    [Fact]
    public void Service_flips_an_existing_deployment_model_in_place()
    {
        using var app = new FakeApp();
        File.AppendAllText(app.Manifest, "\narchitecture:\n  deploymentModel: modular-monolith\n  style: vertical-slice\n");
        var before = app.Read(app.Manifest);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);
        Assert.Equal(before.Replace("modular-monolith", "microservice", StringComparison.Ordinal), app.Read(app.Manifest));
    }

    [Fact]
    public void Service_leaves_a_microservice_manifest_untouched()
    {
        using var app = new FakeApp();
        File.AppendAllText(app.Manifest, "\narchitecture:\n  deploymentModel: microservice\n");
        var before = app.Read(app.Manifest);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);
        Assert.Equal(before, app.Read(app.Manifest));
    }

    // ── service: naming edge cases ───────────────────────────────────────────────────

    [Fact]
    public void Api_project_without_the_Api_suffix_keeps_its_whole_name_as_prefix()
    {
        using var app = new FakeApp();
        File.Move(app.ApiProject, Src(app, "Shop.Api", "Shop.Web.csproj"));
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);

        Assert.True(File.Exists(Src(app, "Shop.Web.BillingService", "Shop.Web.BillingService.csproj")));
        Assert.False(Directory.Exists(Src(app, "Shop.BillingService")));
        Assert.Contains("var Shop_Web_BillingServiceResource = builder.AddProject<Projects.Shop_Web_BillingService>(\"billing-service\")",
            app.Read(app.AppHost), StringComparison.Ordinal);
        Assert.Contains("\"applicationUrl\": \"http://localhost:5334\"",
            File.ReadAllText(Src(app, "Shop.Web.BillingService", "Properties", "launchSettings.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Multi_word_name_derives_kebab_and_db_names()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "order-history").ExitCode);

        var program = File.ReadAllText(Src(app, "Shop.OrderHistoryService", "Program.cs"));
        Assert.Contains("GetConnectionString(\"order-historyservicedb\")", program, StringComparison.Ordinal);
        Assert.Contains("AddGoldpathData<WebApplicationBuilder, OrderHistoryDb>", program, StringComparison.Ordinal);
        Assert.Contains("service = \"order-history-service\"", program, StringComparison.Ordinal);
        Assert.Contains("boundedContext: order-history\n",
            File.ReadAllText(Src(app, "Shop.OrderHistoryService", ".goldpath", "manifest.yaml")), StringComparison.Ordinal);
        Assert.Contains("AddDatabase(\"order-historyservicedb\")", app.Read(app.AppHost), StringComparison.Ordinal);
    }

    // ── service: refusals ────────────────────────────────────────────────────────────

    [Fact]
    public void Service_refuses_a_non_solution_manifest()
    {
        using var app = new FakeApp(kind: "service");
        var runner = new FakeProcessRunner();
        var result = Run(app, runner, "new", "service", "Billing");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("goldpath: this manifest is kind 'service' — service and gateway heads join a SOLUTION's AppHost.\n",
            result.Error.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
        Assert.Empty(runner.Calls);
        Assert.False(Directory.Exists(Src(app, "Shop.BillingService")));
    }

    [Fact]
    public void Gateway_refuses_a_non_solution_manifest()
    {
        using var app = new FakeApp(kind: "worker");
        var runner = new FakeProcessRunner();
        var result = Run(app, runner, "new", "gateway");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("goldpath: this manifest is kind 'worker' — service and gateway heads join a SOLUTION's AppHost.\n",
            result.Error.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Src(app, "Shop.Gateway")));
    }

    [Fact]
    public void Service_refuses_when_no_provider_can_be_inferred()
    {
        using var app = new FakeApp();
        File.WriteAllText(app.ApiProject, """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Goldpath.Abstractions" />
                <!-- goldpath:features packages — the drift profile is the source of these rows -->
              </ItemGroup>
            </Project>
            """);
        var runner = new FakeProcessRunner();
        var result = Run(app, runner, "new", "service", "Billing");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("goldpath: a service head owns a database, and this app's provider could not be inferred — the api csproj references neither Npgsql.EntityFrameworkCore.PostgreSQL nor Microsoft.EntityFrameworkCore.SqlServer.\n",
            result.Error.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Src(app, "Shop.BillingService")));
    }

    [Fact]
    public void Service_refuses_an_existing_project_directory()
    {
        using var app = new FakeApp();
        var projectDir = Src(app, "Shop.BillingService");
        Directory.CreateDirectory(projectDir);
        var runner = new FakeProcessRunner();
        var result = Run(app, runner, "new", "service", "Billing");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal($"goldpath: {projectDir} already exists — pick another name.\n",
            result.Error.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
        Assert.Empty(runner.Calls);
        Assert.True(Directory.Exists(projectDir));   // the refusal never deletes what it found
    }

    [Fact]
    public void Gateway_refuses_a_second_gateway()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "gateway").ExitCode);
        var result = Run(app, runner, "new", "gateway");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal($"goldpath: {Src(app, "Shop.Gateway")} already exists — one gateway per solution.\n",
            result.Error.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_solution_files_are_refused()
    {
        using var app = new FakeApp();
        File.WriteAllText(Path.Combine(app.Root, "Other.sln"), "");
        var runner = new FakeProcessRunner();
        var result = Run(app, runner, "new", "service", "Billing");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal($"goldpath: 2 .sln files at {app.Root} — exactly one expected.\n",
            result.Error.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Src(app, "Shop.BillingService")));
    }

    [Fact]
    public void Other_root_files_do_not_count_as_solutions()
    {
        // A root README beside the .sln: only *.sln is counted, and only *.cs is scanned for
        // the smoke anchor — a markdown note carrying the anchor text is never edited.
        using var app = new FakeApp();
        var notes = Path.Combine(app.Root, "NOTES.md");
        File.WriteAllText(notes, "notes\n// goldpath:smoke heads\n");
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);
        Assert.Equal("notes\n// goldpath:smoke heads\n", File.ReadAllText(notes));
    }

    [Fact]
    public void Failed_sln_add_fails_loudly_and_restores()
    {
        using var app = new FakeApp();
        GiveSmokeAnchor(app);
        var before = new[] { app.Manifest, app.AppHost, app.AppHostProject, SmokePath(app) }
            .ToDictionary(p => p, app.Read, StringComparer.Ordinal);
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["sln"] = 1;

        var result = Run(app, runner, "new", "service", "Billing");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("goldpath: dotnet sln add failed — see the output above.\n",
            result.Error.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
        Assert.Single(runner.Calls);   // the engine never runs on a half-added project
        Assert.False(Directory.Exists(Src(app, "Shop.BillingService")));
        foreach (var (path, text) in before)
        {
            Assert.Equal(text, app.Read(path));
        }
    }

    [Fact]
    public void Gateway_failed_sln_add_fails_loudly_and_restores()
    {
        using var app = new FakeApp();
        var before = new[] { app.Manifest, app.AppHost, app.AppHostProject }
            .ToDictionary(p => p, app.Read, StringComparer.Ordinal);
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["sln"] = 1;

        var result = Run(app, runner, "new", "gateway");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("goldpath: dotnet sln add failed — see the output above.\n",
            result.Error.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
        Assert.Single(runner.Calls);
        Assert.False(Directory.Exists(Src(app, "Shop.Gateway")));
        foreach (var (path, text) in before)
        {
            Assert.Equal(text, app.Read(path));
        }
    }

    // ── the gate: each engine call alone is enough to refuse ─────────────────────────

    [Theory]
    [InlineData("--rules")]                   // only the app-manifest validate carries --rules
    [InlineData("BillingService/.goldpath")]  // only the NEW manifest's validate
    [InlineData("drift")]
    public void Any_single_red_engine_call_refuses_with_the_exact_message(string marker)
    {
        using var app = new FakeApp();
        GiveSmokeAnchor(app);
        var smokeBefore = File.ReadAllText(SmokePath(app));
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain[marker] = 1;

        var result = Run(app, runner, "new", "service", "Billing");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("goldpath: the engine rejected the result — ALL files restored; nothing half-applied.\n",
            result.Error.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
        Assert.Equal(string.Empty, result.Output);
        Assert.False(Directory.Exists(Src(app, "Shop.BillingService")));
        Assert.Equal(smokeBefore, File.ReadAllText(SmokePath(app)));   // the smoke is in the snapshot too
    }

    [Fact]
    public void Red_engine_after_the_gateway_restores_the_gateway_appsettings()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "gateway").ExitCode);
        var settingsPath = Src(app, "Shop.Gateway", "appsettings.json");
        var settingsBefore = File.ReadAllText(settingsPath);
        var appHostBefore = app.Read(app.AppHost);

        runner.ExitCodeWhenArgumentsContain["drift"] = 1;
        Assert.Equal(1, Run(app, runner, "new", "service", "Billing").ExitCode);
        Assert.Equal(settingsBefore, File.ReadAllText(settingsPath));
        Assert.Equal(appHostBefore, app.Read(app.AppHost));
    }

    [Fact]
    public void Gateway_red_engine_restores_everything_byte_identical()
    {
        using var app = new FakeApp();
        GiveSmokeAnchor(app);
        var before = new[] { app.Manifest, app.AppHost, app.AppHostProject, SmokePath(app), Path.Combine(app.Root, "Shop.sln") }
            .ToDictionary(p => p, app.Read, StringComparer.Ordinal);
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["validate"] = 1;

        var result = Run(app, runner, "new", "gateway");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("goldpath: the engine rejected the result — ALL files restored; nothing half-applied.\n",
            result.Error.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Src(app, "Shop.Gateway")));
        foreach (var (path, text) in before)
        {
            Assert.Equal(text, app.Read(path));
        }
    }

    // ── smoke discovery ──────────────────────────────────────────────────────────────

    [Fact]
    public void No_smoke_file_is_fine()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);
        Assert.Equal(0, Run(app, runner, "new", "gateway").ExitCode);
    }

    [Fact]
    public void Smoke_anchor_under_bin_or_obj_is_ignored()
    {
        using var app = new FakeApp();
        var binSmoke = Src(app, "Shop.Api", "bin", "Debug", "SmokeTests.cs");
        var objSmoke = Src(app, "Shop.Api", "obj", "Debug", "SmokeTests.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(binSmoke)!);
        Directory.CreateDirectory(Path.GetDirectoryName(objSmoke)!);
        const string stale = "// goldpath:smoke heads\n";
        File.WriteAllText(binSmoke, stale);
        File.WriteAllText(objSmoke, stale);

        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);
        Assert.Equal(stale, File.ReadAllText(binSmoke));
        Assert.Equal(stale, File.ReadAllText(objSmoke));
    }

    // ── gateway appsettings discovery ────────────────────────────────────────────────

    [Fact]
    public void Only_a_Gateway_directory_is_a_gateway()
    {
        // Sibling projects with a YARP-shaped appsettings are NOT the gateway: nothing is edited.
        using var app = new FakeApp();
        const string yarp = "{ \"ReverseProxy\": { \"Routes\": { \"x\": {} }, \"Clusters\": { \"x\": {} } } }";
        var apiSettings = Src(app, "Shop.Api", "appsettings.json");
        var hostSettings = Src(app, "Shop.AppHost", "appsettings.json");
        File.WriteAllText(apiSettings, yarp);
        File.WriteAllText(hostSettings, yarp);

        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);
        Assert.Equal(yarp, File.ReadAllText(apiSettings));
        Assert.Equal(yarp, File.ReadAllText(hostSettings));
    }

    [Fact]
    public void A_gateway_directory_without_appsettings_is_not_a_gateway()
    {
        using var app = new FakeApp();
        Directory.CreateDirectory(Src(app, "Shop.Gateway"));
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);
        Assert.False(File.Exists(Src(app, "Shop.Gateway", "appsettings.json")));
        Assert.DoesNotContain("WithReference(Shop_BillingServiceResource)", app.Read(app.AppHost), StringComparison.Ordinal);
    }

    [Fact]
    public void A_gateway_without_the_references_anchor_gets_the_route_but_no_reference()
    {
        using var app = new FakeApp();
        Directory.CreateDirectory(Src(app, "Shop.Gateway"));
        var settingsPath = Src(app, "Shop.Gateway", "appsettings.json");
        File.WriteAllText(settingsPath, "{ \"ReverseProxy\": { \"Routes\": { \"api\": {} }, \"Clusters\": { \"api\": {} } } }");

        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);
        Assert.Contains("\"billing-service\": {", File.ReadAllText(settingsPath), StringComparison.Ordinal);
        Assert.DoesNotContain("WithReference(Shop_BillingServiceResource)", app.Read(app.AppHost), StringComparison.Ordinal);
    }

    [Fact]
    public void Service_after_the_gateway_prepends_exact_route_and_cluster_blocks()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "gateway").ExitCode);
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);

        Assert.Equal("""
            {
              "ReverseProxy": {
                "Routes": {
                  "billing-service": {
                    "ClusterId": "billing-service",
                    "Match": { "Path": "/billing-service/{**rest}" },
                    "Transforms": [ { "PathRemovePrefix": "/billing-service" } ]
                  },
                  "api": {
                    "ClusterId": "api",
                    "Match": { "Path": "/api/{**rest}" },
                    "Transforms": [ { "PathRemovePrefix": "/api" } ]
                  }
                },
                "Clusters": {
                  "billing-service": {
                    "Destinations": { "head": { "Address": "https+http://billing-service" } }
                  },
                  "api": {
                    "Destinations": { "head": { "Address": "https+http://api" } }
                  }
                }
              }
            }
            """, File.ReadAllText(Src(app, "Shop.Gateway", "appsettings.json")));

        Assert.Contains(
            "    // goldpath:gateway references — services join here (goldpath new service)\n"
            + "    .WithReference(Shop_BillingServiceResource)\n"
            + "    .WithHttpHealthCheck(\"/health/ready\");\n",
            app.Read(app.AppHost), StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_route_edit_fails_inside_the_guard()
    {
        // Empty Routes/Clusters objects make the prepended comma dangle: the parse must
        // throw BEFORE the write, and the snapshot must put everything back.
        using var app = new FakeApp();
        Directory.CreateDirectory(Src(app, "Shop.Gateway"));
        var settingsPath = Src(app, "Shop.Gateway", "appsettings.json");
        const string empty = "{ \"ReverseProxy\": { \"Routes\": {}, \"Clusters\": {} } }";
        File.WriteAllText(settingsPath, empty);
        var appHostBefore = app.Read(app.AppHost);

        var runner = new FakeProcessRunner();
        Assert.ThrowsAny<JsonException>(() => Run(app, runner, "new", "service", "Billing"));
        Assert.Equal(empty, File.ReadAllText(settingsPath));
        Assert.Equal(appHostBefore, app.Read(app.AppHost));
        Assert.False(Directory.Exists(Src(app, "Shop.BillingService")));
    }

    // ── gateway: generated files, pinned exactly ─────────────────────────────────────

    [Fact]
    public void Gateway_csproj_and_program_are_exact()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "gateway").ExitCode);

        Assert.Equal("""
            <Project Sdk="Microsoft.NET.Sdk.Web">

              <!-- goldpath:service-head — an additional head; the PRIMARY head keeps the anchors. -->
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Goldpath.ServiceDefaults" />
                <PackageReference Include="Yarp.ReverseProxy" />
                <PackageReference Include="Microsoft.Extensions.ServiceDiscovery.Yarp" />
              </ItemGroup>

            </Project>
            """, File.ReadAllText(Src(app, "Shop.Gateway", "Shop.Gateway.csproj")));

        Assert.Equal("""
            using Goldpath;

            // The YARP gateway head (modules: [yarpGateway]): routes /{head}/… to the api and every
            // service over Aspire service discovery — configuration, not code (ADR-0003: YARP is
            // configured, never wrapped). Routes live in appsettings; goldpath new service appends.
            var builder = WebApplication.CreateBuilder(args);

            builder.AddGoldpathServiceDefaults();
            builder.Services.AddServiceDiscovery();   // the resolver below needs the discovery CORE (config provider)
            builder.Services.AddReverseProxy()
                .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
                .AddServiceDiscoveryDestinationResolver();

            var app = builder.Build();

            app.MapGoldpathDefaultEndpoints();
            app.MapReverseProxy();

            app.Run();
            """, File.ReadAllText(Src(app, "Shop.Gateway", "Program.cs")));
    }

    [Fact]
    public void Gateway_launch_settings_and_manifest_are_exact()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "gateway").ExitCode);

        // 5300 + |Σ c*31| % 200 for "Shop.Gateway".
        Assert.Equal("""
            {
              "profiles": {
                "Shop.Gateway": {
                  "commandName": "Project",
                  "applicationUrl": "http://localhost:5418"
                }
              }
            }
            """, File.ReadAllText(Src(app, "Shop.Gateway", "Properties", "launchSettings.json")));

        Assert.Equal("""
            schemaVersion: 1
            kind: gateway
            name: Shop.Gateway
            description: YARP gateway — routes /{head}/… to the api and every service
            owner: platform-team
            autoRegisterServices: true
            """, File.ReadAllText(Src(app, "Shop.Gateway", ".goldpath", "manifest.yaml")));
    }

    [Fact]
    public void Gateway_after_a_service_routes_both_heads_exactly()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "service", "Billing").ExitCode);
        var result = Run(app, runner, "new", "gateway");
        Assert.Equal(0, result.ExitCode);

        Assert.Equal("""
            {
              "ReverseProxy": {
                "Routes": {
                  "api": {
                    "ClusterId": "api",
                    "Match": { "Path": "/api/{**rest}" },
                    "Transforms": [ { "PathRemovePrefix": "/api" } ]
                  },
                  "billing-service": {
                    "ClusterId": "billing-service",
                    "Match": { "Path": "/billing-service/{**rest}" },
                    "Transforms": [ { "PathRemovePrefix": "/billing-service" } ]
                  }
                },
                "Clusters": {
                  "api": {
                    "Destinations": { "head": { "Address": "https+http://api" } }
                  },
                  "billing-service": {
                    "Destinations": { "head": { "Address": "https+http://billing-service" } }
                  }
                }
              }
            }
            """, File.ReadAllText(Src(app, "Shop.Gateway", "appsettings.json")));

        // Composed LAST: every reference by its named variable, just above Build().Run().
        Assert.EndsWith("""
            builder.AddProject<Projects.Shop_Gateway>("gateway")
                .WithReference(api)
                .WithReference(Shop_BillingServiceResource)
                // goldpath:gateway references — services join here (goldpath new service)
                .WithHttpHealthCheck("/health/ready");
            builder.Build().Run();
            """, app.Read(app.AppHost), StringComparison.Ordinal);
        Assert.StartsWith("var builder = DistributedApplication.CreateBuilder(args);", app.Read(app.AppHost), StringComparison.Ordinal);

        Assert.Equal(
            "── goldpath new gateway: Shop.Gateway — YARP over Aspire service discovery; routes /{head}/… for: api, billing-service\n"
            + "   new services register their route automatically (goldpath new service edits the gateway's appsettings).\n",
            result.Output.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void Gateway_wires_its_csproj_reference_and_calls_the_engine_on_its_manifest()
    {
        using var app = new FakeApp();
        var projectBefore = app.Read(app.AppHostProject);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "gateway").ExitCode);

        const string referenceAnchor = "    <!-- goldpath:workers references — worker projects chain here (goldpath add worker) -->";
        Assert.Equal(
            projectBefore.Replace(referenceAnchor,
                referenceAnchor + "\n    <ProjectReference Include=\"../Shop.Gateway/Shop.Gateway.csproj\" />",
                StringComparison.Ordinal),
            app.Read(app.AppHostProject));

        Assert.Equal(4, runner.Calls.Count);
        Assert.Equal("dotnet", runner.Calls[0].FileName);
        Assert.Equal(["sln", Path.Combine(app.Root, "Shop.sln"), "add", Src(app, "Shop.Gateway", "Shop.Gateway.csproj")], runner.Calls[0].Arguments);
        Assert.Equal(app.Root, runner.Calls[0].WorkingDirectory);
        Assert.Equal("validate", runner.Calls[2].Arguments[0]);
        Assert.Equal(Path.Combine("src", "Shop.Gateway", ".goldpath", "manifest.yaml"), runner.Calls[2].Arguments[1]);
        Assert.Equal("drift", runner.Calls[3].Arguments[0]);
    }

    [Fact]
    public void Gateway_appends_the_exact_smoke_block_with_the_routed_probe()
    {
        using var app = new FakeApp();
        GiveSmokeAnchor(app);
        var before = File.ReadAllText(SmokePath(app));
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "gateway").ExitCode);

        Assert.Equal(before.Replace(SmokeAnchorLine, SmokeAnchorLine + "\n" + """
                    var gatewayClient = app.CreateHttpClient("gateway");
                    await WaitUntilAsync(async () =>
                        (await gatewayClient.GetAsync("/health/ready", timeout.Token)).IsSuccessStatusCode, timeout.Token);
                    // Routed THROUGH the head: a 2xx here is the whole chain answering.
                    Assert.True((await gatewayClient.GetAsync("/api/health/ready", timeout.Token)).IsSuccessStatusCode);
            """, StringComparison.Ordinal), File.ReadAllText(SmokePath(app)));
    }

    [Fact]
    public void Gateway_routes_only_the_api_and_service_heads()
    {
        using var app = new FakeApp();
        File.WriteAllText(app.AppHost, app.Read(app.AppHost).Replace(
            "// goldpath:workers — additional worker projects wire here (goldpath add worker)",
            "// goldpath:workers — additional worker projects wire here (goldpath add worker)\n"
            + "var eod = builder.AddProject<Projects.Shop_EodWorker>(\"eod-worker\");\n",
            StringComparison.Ordinal));

        var runner = new FakeProcessRunner();
        var result = Run(app, runner, "new", "gateway");
        Assert.Equal(0, result.ExitCode);
        var settings = File.ReadAllText(Src(app, "Shop.Gateway", "appsettings.json"));
        Assert.DoesNotContain("eod-worker", settings, StringComparison.Ordinal);
        Assert.Contains("\"api\": {\n        \"ClusterId\": \"api\",", settings, StringComparison.Ordinal);
        Assert.Contains("for: api\n", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("WithReference(eod)", app.Read(app.AppHost), StringComparison.Ordinal);
    }

    [Fact]
    public void Gateway_manifest_normalises_crlf_and_declares_the_module_once()
    {
        using var app = new FakeApp();
        var crlf = app.Read(app.Manifest).Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
        File.WriteAllText(app.Manifest, crlf);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "gateway").ExitCode);
        Assert.Equal(crlf.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\nmodules: [yarpGateway]\n", app.Read(app.Manifest));
    }

    [Fact]
    public void Gateway_leaves_a_manifest_that_already_declares_yarpGateway_untouched()
    {
        using var app = new FakeApp();
        File.AppendAllText(app.Manifest, "\nmodules: [yarpGateway]\n");
        var before = app.Read(app.Manifest);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "gateway").ExitCode);
        Assert.Equal(before, app.Read(app.Manifest));
    }

    [Fact]
    public void Gateway_needs_the_build_run_line_to_land_last()
    {
        using var app = new FakeApp();
        var appHostBefore = app.Read(app.AppHost).Replace("builder.Build().Run();", "await builder.Build().RunAsync();", StringComparison.Ordinal);
        File.WriteAllText(app.AppHost, appHostBefore);
        var runner = new FakeProcessRunner();

        var result = Run(app, runner, "new", "gateway");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("goldpath: the AppHost has no 'builder.Build().Run();' line — cannot place the gateway last.\n",
            result.Error.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
        Assert.Equal(appHostBefore, app.Read(app.AppHost));
        Assert.False(Directory.Exists(Src(app, "Shop.Gateway")));
    }

    [Fact]
    public void Gateway_refuses_a_service_head_without_a_named_variable()
    {
        using var app = new FakeApp();
        var appHostBefore = app.Read(app.AppHost).Replace(
            "// goldpath:workers — additional worker projects wire here (goldpath add worker)",
            "// goldpath:workers — additional worker projects wire here (goldpath add worker)\n"
            + "builder.AddProject<Projects.Shop_OrdersService>(\"orders-service\");\n",
            StringComparison.Ordinal);
        File.WriteAllText(app.AppHost, appHostBefore);
        var runner = new FakeProcessRunner();

        var result = Run(app, runner, "new", "gateway");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("goldpath: the 'orders-service' head has no named resource variable in the AppHost — regenerate the service with a current goldpath, or name the chain's variable.\n",
            result.Error.Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
        Assert.Equal(appHostBefore, app.Read(app.AppHost));
        Assert.False(Directory.Exists(Src(app, "Shop.Gateway")));
    }

    [Fact]
    public void Gateway_references_the_api_by_its_literal_name_even_when_the_chain_is_not_renamed()
    {
        // The api handle is `api` by CONTRACT: a chain the regex cannot rename (no _Api
        // project suffix) still gets `.WithReference(api)` — never a HeadVar lookup.
        using var app = new FakeApp();
        File.WriteAllText(app.AppHost, app.Read(app.AppHost).Replace("Projects.Shop_Api", "Projects.Shop_Web", StringComparison.Ordinal));
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Run(app, runner, "new", "gateway").ExitCode);
        var appHost = app.Read(app.AppHost);
        Assert.Contains("\nbuilder.AddProject<Projects.Shop_Web>(\"api\")\n", appHost, StringComparison.Ordinal);
        Assert.Contains("builder.AddProject<Projects.Shop_Gateway>(\"gateway\")\n    .WithReference(api)\n", appHost, StringComparison.Ordinal);
    }
}
