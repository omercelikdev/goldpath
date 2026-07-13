using Xunit;

namespace Goldpath.Cli.Tests;

public class DbCommandTests
{
    private static int Db(FakeApp app, FakeProcessRunner runner, params string[] verb)
        => CliRunner.Run(["db", .. verb, "--path", app.Root], runner, TextWriter.Null, TextWriter.Null);

    [Fact]
    public void Init_restores_the_tool_then_adds_Initial_per_owner()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Db(app, runner, "init"));

        Assert.Equal(["tool", "restore"], runner.Calls[0].Arguments);
        Assert.Equal(["restore"], runner.Calls[1].Arguments);   // `dotnet ef` builds but never restores
        var ef = runner.Calls[2];
        Assert.Equal("dotnet", ef.FileName);
        Assert.Equal("ef", ef.Arguments[0]);
        Assert.Equal(["migrations", "add", "Initial"], ef.Arguments.Skip(1).Take(3));
        Assert.Contains(ef.Arguments, a => a.EndsWith("Shop.Api.csproj", StringComparison.Ordinal));
        Assert.Contains("--startup-project", ef.Arguments);
    }

    [Fact]
    public void Init_is_idempotent_when_Migrations_already_exists()
    {
        using var app = new FakeApp();
        Directory.CreateDirectory(Path.Combine(app.Root, "src", "Shop.Api", "Migrations"));
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Db(app, runner, "init"));
        Assert.DoesNotContain(runner.Calls, c => c.Arguments.Contains("migrations"));   // restore only
    }

    [Fact]
    public void Add_needs_a_name_and_fans_out_with_it()
    {
        using var app = new FakeApp();
        Assert.Equal(2, Db(app, new FakeProcessRunner(), "add"));   // usage error

        var runner = new FakeProcessRunner();
        Assert.Equal(0, Db(app, runner, "add", "add-campaign"));
        // Kebab input is normalized: EF would otherwise emit an all-lowercase class (CS8981).
        Assert.Contains(runner.Calls, c => c.Arguments.Contains("AddCampaign"));
        Assert.DoesNotContain(runner.Calls, c => c.Arguments.Contains("add-campaign"));
    }

    [Fact]
    public void Status_goes_red_with_teaching_when_the_model_outran_the_migrations()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["has-pending-model-changes"] = 1;
        var error = new StringWriter();

        Assert.Equal(1, CliRunner.Run(["db", "status", "--path", app.Root], runner, TextWriter.Null, error));
        Assert.Contains("run goldpath db add", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Bundle_produces_one_artifact_per_owner()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Db(app, runner, "bundle"));
        var bundle = Assert.Single(runner.Calls, c => c.Arguments.Contains("bundle"));
        Assert.Contains(bundle.Arguments, a => a.EndsWith(Path.Combine("Shop.Api-migrations"), StringComparison.Ordinal));
        Assert.Contains("--force", bundle.Arguments);
    }

    [Fact]
    public void Ownerless_apps_no_op_on_init_and_status_but_teach_on_add()
    {
        using var app = new FakeApp();
        var api = Path.Combine(app.Root, "src", "Shop.Api", "Shop.Api.csproj");
        File.WriteAllText(api, File.ReadAllText(api).Replace("Microsoft.EntityFrameworkCore.Design", "Nothing.Here"));

        Assert.Equal(0, Db(app, new FakeProcessRunner(), "init"));
        Assert.Equal(0, Db(app, new FakeProcessRunner(), "status"));
        Assert.Equal(1, Db(app, new FakeProcessRunner(), "add", "x"));
    }

    [Fact]
    public void Check_runs_db_status_between_drift_and_build()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, CliRunner.Run(["check", "--path", app.Root], runner, TextWriter.Null, TextWriter.Null));
        var flat = runner.Calls.Select(c => string.Join(' ', c.Arguments)).ToList();
        var statusIndex = flat.FindIndex(a => a.Contains("has-pending-model-changes", StringComparison.Ordinal));
        var buildIndex = flat.FindIndex(a => a.StartsWith("build", StringComparison.Ordinal));
        Assert.True(statusIndex >= 0 && buildIndex > statusIndex, "db status must gate the build");
    }

    [Fact]
    public void New_with_output_runs_the_Initial_migration_in_the_generated_app()
    {
        using var app = new FakeApp();   // stands in for the freshly generated app
        var runner = new FakeProcessRunner();
        Assert.Equal(0, CliRunner.Run(["new", "solution", "-n", "Shop", "-o", app.Root], runner, TextWriter.Null, TextWriter.Null));

        Assert.Equal(["new", "goldpath-solution", "-n", "Shop", "-o", app.Root], runner.Calls[0].Arguments);
        Assert.Contains(runner.Calls, c => c.Arguments.Contains("migrations") && c.Arguments.Contains("Initial"));
    }

    [Fact]
    public void New_commits_the_first_OpenAPI_contract_into_specs()
    {
        // Issue #12: SPEC0211 demands a committed contract, but generation left specs/
        // empty — the post-step copies the build-time export the db init just produced.
        using var app = new FakeApp();
        var openapi = Path.Combine(app.Root, "src/Shop.Api/openapi");
        Directory.CreateDirectory(openapi);
        File.WriteAllText(Path.Combine(openapi, "Shop.Api.json"), "{}");
        Directory.CreateDirectory(Path.Combine(app.Root, "specs"));
        File.WriteAllText(Path.Combine(app.Root, "specs", "Existing.json"), "keep");
        var output = new StringWriter();

        Assert.Equal(0, CliRunner.Run(["new", "solution", "-n", "Shop", "-o", app.Root], new FakeProcessRunner(), output, TextWriter.Null));

        Assert.Equal("{}", File.ReadAllText(Path.Combine(app.Root, "specs", "Shop.Api.json")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(app.Root, "specs", "Existing.json")));   // never clobbered
        Assert.Contains("first OpenAPI contract committed", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Add_feature_with_model_calls_ends_with_the_db_add_step()
    {
        using var app = new FakeApp(messagingWired: true, jobsWired: true);
        var output = new StringWriter();
        Assert.Equal(0, CliRunner.Run(["add", "feature", "campaign", "--path", app.Root], new FakeProcessRunner(), output, TextWriter.Null));
        Assert.Contains("goldpath db add AddCampaign", output.ToString(), StringComparison.Ordinal);
    }
}
