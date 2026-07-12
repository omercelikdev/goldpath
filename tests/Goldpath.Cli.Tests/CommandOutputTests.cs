using Xunit;

namespace Goldpath.Cli.Tests;

/// <summary>
/// The CLI's words are part of its contract: progress lines tell the team what ran, next
/// steps carry the domain decisions goldpath refuses to guess. Locked here so mutants that
/// silence or garble them die.
/// </summary>
public class CommandOutputTests
{
    [Fact]
    public void Check_narrates_each_step_and_declares_green()
    {
        using var app = new FakeApp();
        var output = new StringWriter();

        CliRunner.Run(["check", "--path", app.Root], new FakeProcessRunner(), output, TextWriter.Null);

        var text = output.ToString();
        var validate = text.IndexOf("── goldpath check: specdrift validate", StringComparison.Ordinal);
        var drift = text.IndexOf("── goldpath check: specdrift drift", StringComparison.Ordinal);
        var build = text.IndexOf("── goldpath check: dotnet build", StringComparison.Ordinal);
        var green = text.IndexOf("── goldpath check GREEN", StringComparison.Ordinal);
        Assert.True(validate >= 0 && validate < drift && drift < build && build < green,
            $"steps must narrate in order; got:\n{text}");
    }

    [Fact]
    public void Check_does_not_declare_green_when_the_build_is_red()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["build"] = 1;
        var output = new StringWriter();

        Assert.Equal(1, CliRunner.Run(["check", "--path", app.Root], runner, output, TextWriter.Null));
        Assert.DoesNotContain("GREEN", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Check_stops_before_build_when_drift_is_red()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["--repo"] = 1;   // fails only the drift call

        Assert.Equal(1, CliRunner.Run(["check", "--path", app.Root], runner, TextWriter.Null, TextWriter.Null));
        Assert.Equal(2, runner.Calls.Count);   // validate + drift, never build
    }

    [Fact]
    public void Add_narrates_wiring_then_engine_then_the_next_steps()
    {
        using var app = new FakeApp();
        var output = new StringWriter();

        CliRunner.Run(["add", "feature", "multitenancy", "--path", app.Root], new FakeProcessRunner(), output, TextWriter.Null);

        var text = output.ToString();
        Assert.Contains("goldpath: 'multitenancy' wired — running the engine (specdrift validate + drift)", text, StringComparison.Ordinal);
        Assert.Contains("goldpath: 'multitenancy' added — engine clean. Your decisions (goldpath never guesses domain opt-ins):", text, StringComparison.Ordinal);
        Assert.Contains("  → mark tenant-owned entities", text, StringComparison.Ordinal);
        Assert.Contains("  → fail-closed from now on", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_reports_already_enabled_by_manifest_key()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        CliRunner.Run(["add", "feature", "caching", "--path", app.Root], runner, TextWriter.Null, TextWriter.Null);
        var output = new StringWriter();

        Assert.Equal(0, CliRunner.Run(["add", "feature", "caching", "--path", app.Root], runner, output, TextWriter.Null));
        Assert.Contains("goldpath: 'caching' is already enabled (distributedCaching) — nothing to do.", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Add_on_engine_rejection_says_restored_and_names_the_feature()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["validate"] = 1;
        var error = new StringWriter();

        Assert.Equal(1, CliRunner.Run(["add", "feature", "softdelete", "--path", app.Root], runner, TextWriter.Null, error));

        Assert.Contains("ALL files restored", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("'softdelete' was NOT added", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Add_skips_drift_when_validate_is_already_red()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["validate"] = 1;

        CliRunner.Run(["add", "feature", "softdelete", "--path", app.Root], runner, TextWriter.Null, TextWriter.Null);

        Assert.DoesNotContain(runner.Calls, c => c.Arguments.Contains("drift"));
    }

    [Fact]
    public void Usage_errors_carry_the_goldpath_prefix_and_the_offending_input()
    {
        using var app = new FakeApp();
        var error = new StringWriter();
        Assert.Equal(2, CliRunner.Run(["add", "feature", "quantumsafe", "--path", app.Root], new FakeProcessRunner(), TextWriter.Null, error));
        Assert.Contains("goldpath: unknown feature 'quantumsafe' — one of: multitenancy, audittrail", error.ToString(), StringComparison.Ordinal);
    }
}
