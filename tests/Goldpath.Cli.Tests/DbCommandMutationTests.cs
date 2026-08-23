using Xunit;

namespace Goldpath.Cli.Tests;

/// <summary>
/// Mutation-killing companions to <see cref="DbCommandTests"/>: the exact <c>dotnet ef</c>
/// argument lists (in order, with the app root as CWD), every console line and every refusal —
/// the CLI is thin over the tool, so the invocation IS the behaviour.
/// </summary>
public class DbCommandMutationTests
{
    private sealed record Result(int Code, string Output, string Error);

    /// <summary>Scripts exit codes by EXACT argument list — the marker dictionary cannot tell `restore` from `tool restore`.</summary>
    private sealed class ExactRunner(Func<IReadOnlyList<string>, int> exitCode) : IProcessRunner
    {
        public List<ProcessCall> Calls { get; } = [];

        public int Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
        {
            Calls.Add(new ProcessCall(fileName, arguments, workingDirectory));
            return exitCode(arguments);
        }
    }

    private static Result Db(FakeApp app, IProcessRunner runner, params string[] verb)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = CliRunner.Run(["db", .. verb, "--path", app.Root], runner, output, error);
        return new Result(code, output.ToString(), error.ToString());
    }

    private static Result Check(FakeApp app, IProcessRunner runner)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = CliRunner.Run(["check", "--path", app.Root], runner, output, error);
        return new Result(code, output.ToString(), error.ToString());
    }

    private static string Line(string text) => text + Environment.NewLine;

    private static string Owner(FakeApp app) => Path.Combine(app.Root, "src", "Shop.Api", "Shop.Api.csproj");

    private static string OwnerRel => Path.Combine("src", "Shop.Api", "Shop.Api.csproj");

    private static void DropOwner(FakeApp app)
        => File.WriteAllText(app.ApiProject, app.Read(app.ApiProject).Replace("Microsoft.EntityFrameworkCore.Design", "Nothing.Here", StringComparison.Ordinal));

    private static string[] Ef(FakeApp app, params string[] args)
        => ["ef", .. args, "--project", Owner(app), "--startup-project", Owner(app)];

    // ── the door: owners, restores, verbs ──────────────────────────────────────────────

    [Theory]
    [InlineData("init")]
    [InlineData("status")]
    public void Ownerless_init_and_status_say_nothing_to_do(string verb)
    {
        using var app = new FakeApp();
        DropOwner(app);
        var runner = new FakeProcessRunner();
        var result = Db(app, runner, verb);
        Assert.Equal(0, result.Code);
        Assert.Equal(Line("── goldpath db: no migration owner (no project references Microsoft.EntityFrameworkCore.Design) — nothing to do."), result.Output);
        Assert.Empty(runner.Calls);   // no restore, no ef
    }

    [Theory]
    [InlineData("add", "x")]
    [InlineData("bundle")]
    public void Ownerless_add_and_bundle_teach_the_Design_reference(params string[] verb)
    {
        using var app = new FakeApp();
        DropOwner(app);
        var result = Db(app, new FakeProcessRunner(), verb);
        Assert.Equal(1, result.Code);
        Assert.Equal(Line("goldpath: no migration owner found — a project owns migrations by referencing Microsoft.EntityFrameworkCore.Design; regenerate from a current template or add the reference to the project that owns the schema."), result.Error);
    }

    [Fact]
    public void Every_verb_opens_with_tool_restore_then_restore_in_the_app_root()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Db(app, runner, "status").Code);

        Assert.Equal("dotnet", runner.Calls[0].FileName);
        Assert.Equal(["tool", "restore"], runner.Calls[0].Arguments);
        Assert.Equal(app.Root, runner.Calls[0].WorkingDirectory);
        Assert.Equal("dotnet", runner.Calls[1].FileName);
        Assert.Equal(["restore"], runner.Calls[1].Arguments);
        Assert.Equal(app.Root, runner.Calls[1].WorkingDirectory);
    }

    [Fact]
    public void A_failed_tool_restore_stops_before_restore()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["tool"] = 1;
        var result = Db(app, runner, "init");
        Assert.Equal(1, result.Code);
        Assert.Equal(Line("goldpath: dotnet tool restore failed — the pinned dotnet-ef tool comes from .config/dotnet-tools.json; see the output above."), result.Error);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public void A_failed_restore_teaches_the_package_feed()
    {
        using var app = new FakeApp();
        var runner = new ExactRunner(args => args.SequenceEqual(["restore"]) ? 1 : 0);
        var result = Db(app, runner, "init");
        Assert.Equal(1, result.Code);
        Assert.Equal(Line("goldpath: dotnet restore failed — wire your package feed (nuget.config), then re-run."), result.Error);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public void Add_without_a_name_is_a_usage_error_with_the_shape()
    {
        using var app = new FakeApp();
        var result = Db(app, new FakeProcessRunner(), "add");
        Assert.Equal(2, result.Code);
        Assert.Equal(Line("goldpath: goldpath db add needs a name: goldpath db add <migration-name>"), result.Error);
    }

    [Fact]
    public void An_unknown_verb_lists_the_four()
    {
        using var app = new FakeApp();
        var result = Db(app, new FakeProcessRunner(), "frobnicate");
        Assert.Equal(2, result.Code);
        Assert.Equal(Line("goldpath: unknown db verb 'frobnicate' — one of: init, add, status, bundle"), result.Error);
    }

    // ── init ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Init_narrates_the_Initial_migration_and_the_done_line()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        var result = Db(app, runner, "init");
        Assert.Equal(0, result.Code);
        Assert.Equal(
            Line($"── goldpath db init: Initial migration for {OwnerRel}")
            + Line("── goldpath db init: done — Development now migrates from these; production applies the bundle"),
            result.Output);
        Assert.Equal(3, runner.Calls.Count);
        Assert.Equal("dotnet", runner.Calls[2].FileName);
        Assert.Equal(Ef(app, "migrations", "add", "Initial"), runner.Calls[2].Arguments);
        Assert.Equal(app.Root, runner.Calls[2].WorkingDirectory);
    }

    [Fact]
    public void Init_skips_an_owner_with_Migrations_but_still_finishes()
    {
        using var app = new FakeApp();
        Directory.CreateDirectory(Path.Combine(app.Root, "src", "Shop.Api", "Migrations"));
        var runner = new FakeProcessRunner();
        var result = Db(app, runner, "init");
        Assert.Equal(0, result.Code);
        Assert.Equal(
            Line($"── goldpath db init: {OwnerRel} already has Migrations/ — skipped")
            + Line("── goldpath db init: done — Development now migrates from these; production applies the bundle"),
            result.Output);
        Assert.Equal(2, runner.Calls.Count);   // restores only
    }

    [Fact]
    public void Init_returns_the_ef_exit_code_and_never_says_done()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["Initial"] = 7;
        var result = Db(app, runner, "init");
        Assert.Equal(7, result.Code);
        Assert.Equal(Line($"── goldpath db init: Initial migration for {OwnerRel}"), result.Output);
    }

    // ── the first-contract commit ──────────────────────────────────────────────────────

    [Fact]
    public void The_first_contract_commit_copies_only_json_exports()
    {
        using var app = new FakeApp();
        var openapi = Path.Combine(app.Root, "src", "Shop.Api", "openapi");
        Directory.CreateDirectory(openapi);
        File.WriteAllText(Path.Combine(openapi, "Shop.Api.json"), "{}");
        File.WriteAllText(Path.Combine(openapi, "notes.txt"), "not a contract");
        var result = Db(app, new FakeProcessRunner(), "init");
        Assert.Equal(0, result.Code);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(app.Root, "specs", "Shop.Api.json")));
        Assert.False(File.Exists(Path.Combine(app.Root, "specs", "notes.txt")));
        Assert.EndsWith(Line("goldpath: first OpenAPI contract committed to specs/Shop.Api.json"), result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void An_already_committed_contract_is_never_clobbered()
    {
        using var app = new FakeApp();
        var openapi = Path.Combine(app.Root, "src", "Shop.Api", "openapi");
        Directory.CreateDirectory(openapi);
        File.WriteAllText(Path.Combine(openapi, "Shop.Api.json"), "{}");
        Directory.CreateDirectory(Path.Combine(app.Root, "specs"));
        File.WriteAllText(Path.Combine(app.Root, "specs", "Shop.Api.json"), "edited by hand");
        var result = Db(app, new FakeProcessRunner(), "init");
        Assert.Equal(0, result.Code);
        Assert.Equal("edited by hand", File.ReadAllText(Path.Combine(app.Root, "specs", "Shop.Api.json")));
        Assert.DoesNotContain("first OpenAPI contract committed", result.Output, StringComparison.Ordinal);
    }

    // ── add ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_probes_pending_changes_then_adds_with_the_exact_arguments()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["has-pending-model-changes"] = 1;
        var result = Db(app, runner, "add", "AddThing");
        Assert.Equal(0, result.Code);
        Assert.Equal(Line($"── goldpath db add: 'AddThing' for {OwnerRel}"), result.Output);
        Assert.Equal(4, runner.Calls.Count);
        Assert.Equal(Ef(app, "migrations", "has-pending-model-changes"), runner.Calls[2].Arguments);
        Assert.Equal(Ef(app, "migrations", "add", "AddThing"), runner.Calls[3].Arguments);
        Assert.All(runner.Calls.Skip(2), c => Assert.Equal("dotnet", c.FileName));
        Assert.All(runner.Calls.Skip(2), c => Assert.Equal(app.Root, c.WorkingDirectory));
    }

    [Fact]
    public void Add_returns_the_ef_exit_code_when_the_migration_fails()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["has-pending-model-changes"] = 1;
        runner.ExitCodeWhenArgumentsContain["AddThing"] = 5;
        Assert.Equal(5, Db(app, runner, "add", "AddThing").Code);
    }

    [Fact]
    public void Add_skip_line_names_the_owner()
    {
        using var app = new FakeApp();
        var result = Db(app, new FakeProcessRunner(), "add", "AddThing");   // exit 0 = model unchanged
        Assert.Equal(0, result.Code);
        Assert.Equal(Line($"── goldpath db add: {OwnerRel} model unchanged — skipped (no empty migration)"), result.Output);
    }

    // ── status ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Status_probes_every_owner_and_reports_green_exactly()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        var result = Db(app, runner, "status");
        Assert.Equal(0, result.Code);
        Assert.Equal(Line("── goldpath db status: every owner's migrations match its model"), result.Output);
        Assert.Equal(string.Empty, result.Error);
        Assert.Equal(3, runner.Calls.Count);
        Assert.Equal(Ef(app, "migrations", "has-pending-model-changes"), runner.Calls[2].Arguments);
    }

    [Fact]
    public void Status_names_every_pending_owner_in_the_red_line()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["has-pending-model-changes"] = 1;
        var result = Db(app, runner, "status");
        Assert.Equal(1, result.Code);
        Assert.Equal(string.Empty, result.Output);
        Assert.Equal(Line($"goldpath db status: the model changed but no migration captures it in: {OwnerRel} — run goldpath db add <name>."), result.Error);
    }

    // ── bundle ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Bundle_defaults_to_artifacts_migrations_under_the_app_root()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        var result = Db(app, runner, "bundle");
        Assert.Equal(0, result.Code);
        var target = Path.Combine(app.Root, "artifacts", "migrations");
        Assert.Equal(
            Line("── goldpath db bundle: Shop.Api")
            + Line($"── goldpath db bundle: artifacts in {target} — deployment runs these BEFORE the new app version starts (never the app process)"),
            result.Output);
        Assert.Equal(3, runner.Calls.Count);
        Assert.Equal(Ef(app, "migrations", "bundle", "--force", "--output", Path.Combine(target, "Shop.Api-migrations")), runner.Calls[2].Arguments);
        Assert.Equal(app.Root, runner.Calls[2].WorkingDirectory);
    }

    [Fact]
    public void Bundle_honours_an_explicit_output_directory()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        var dist = Path.Combine(app.Root, "dist");
        var result = Db(app, runner, "bundle", dist);
        Assert.Equal(0, result.Code);
        Assert.Equal(Ef(app, "migrations", "bundle", "--force", "--output", Path.Combine(dist, "Shop.Api-migrations")), runner.Calls[2].Arguments);
        Assert.Contains(Line($"── goldpath db bundle: artifacts in {dist} — deployment runs these BEFORE the new app version starts (never the app process)"), result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Bundle_returns_the_ef_exit_code_without_the_artifacts_line()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["bundle"] = 3;
        var result = Db(app, runner, "bundle");
        Assert.Equal(3, result.Code);
        Assert.Equal(Line("── goldpath db bundle: Shop.Api"), result.Output);
    }

    // ── goldpath check's hook ──────────────────────────────────────────────────────────

    [Fact]
    public void Check_skips_the_db_step_entirely_on_an_ownerless_app()
    {
        using var app = new FakeApp();
        DropOwner(app);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Check(app, runner).Code);
        Assert.DoesNotContain(runner.Calls, c => c.Arguments.Contains("restore"));
        Assert.DoesNotContain(runner.Calls, c => c.Arguments.Contains("ef"));
        Assert.Contains(runner.Calls, c => c.Arguments.Contains("build"));   // the build still ran
    }

    [Fact]
    public void Check_goes_red_when_tool_restore_fails()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["tool"] = 1;
        var result = Check(app, runner);
        Assert.Equal(1, result.Code);
        Assert.Equal(Line("goldpath check: dotnet tool restore failed — the pinned dotnet-ef tool comes from .config/dotnet-tools.json."), result.Error);
        var restore = Assert.Single(runner.Calls, c => c.Arguments.Contains("tool"));
        Assert.Equal("dotnet", restore.FileName);
        Assert.Equal(["tool", "restore"], restore.Arguments);
        Assert.Equal(app.Root, restore.WorkingDirectory);
        Assert.DoesNotContain(runner.Calls, c => c.Arguments.Contains("build"));
    }

    [Fact]
    public void Check_goes_red_when_restore_fails()
    {
        using var app = new FakeApp();
        var runner = new ExactRunner(args => args.SequenceEqual(["restore"]) ? 1 : 0);
        var result = Check(app, runner);
        Assert.Equal(1, result.Code);
        Assert.Equal(Line("goldpath check: dotnet restore failed — wire your package feed (nuget.config)."), result.Error);
        Assert.DoesNotContain(runner.Calls, c => c.Arguments.Contains("ef"));
    }

    // ── owner discovery ────────────────────────────────────────────────────────────────

    [Fact]
    public void Owners_fan_out_in_ordinal_path_order()
    {
        using var app = new FakeApp();
        var worker = Path.Combine(app.Root, "src", "Shop.Worker");
        Directory.CreateDirectory(worker);
        File.WriteAllText(Path.Combine(worker, "Shop.Worker.csproj"), "<PackageReference Include=\"Microsoft.EntityFrameworkCore.Design\" />");
        var runner = new FakeProcessRunner();
        var result = Db(app, runner, "init");
        Assert.Equal(0, result.Code);
        var owners = runner.Calls.Skip(2).Select(c => c.Arguments[^1]).ToList();
        Assert.Equal([Owner(app), Path.Combine(worker, "Shop.Worker.csproj")], owners);
        Assert.StartsWith(
            Line($"── goldpath db init: Initial migration for {OwnerRel}")
            + Line($"── goldpath db init: Initial migration for {Path.Combine("src", "Shop.Worker", "Shop.Worker.csproj")}"),
            result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_csproj_files_outside_bin_and_obj_can_own_migrations()
    {
        using var app = new FakeApp();
        var api = Path.Combine(app.Root, "src", "Shop.Api");
        // A doc MENTIONING the package, and build outputs carrying a copy of the csproj: none are owners.
        File.WriteAllText(Path.Combine(api, "README.md"), "references Microsoft.EntityFrameworkCore.Design");
        foreach (var shadow in new[] { Path.Combine(api, "bin", "Debug"), Path.Combine(api, "obj", "Debug") })
        {
            Directory.CreateDirectory(shadow);
            File.Copy(app.ApiProject, Path.Combine(shadow, "Shop.Api.csproj"));
        }

        var runner = new FakeProcessRunner();
        Assert.Equal(0, Db(app, runner, "init").Code);
        var ef = Assert.Single(runner.Calls, c => c.Arguments.Contains("Initial"));
        Assert.Equal(Owner(app), ef.Arguments[^1]);
    }
}
