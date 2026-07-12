using Xunit;

namespace Goldpath.Cli.Tests;

public class CliRunnerTests
{
    [Fact]
    public void No_arguments_prints_usage_and_exits_2()
    {
        var error = new StringWriter();
        Assert.Equal(2, CliRunner.Run([], new FakeProcessRunner(), TextWriter.Null, error));
        Assert.Contains("goldpath new solution|worker", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void New_solution_passes_through_to_dotnet_new_with_all_arguments()
    {
        var runner = new FakeProcessRunner();
        var exitCode = CliRunner.Run(
            ["new", "solution", "-n", "Shop", "--features", "audittrail", "--auth", "none"],
            runner, TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("dotnet", call.FileName);
        Assert.Equal(["new", "goldpath-solution", "-n", "Shop", "--features", "audittrail", "--auth", "none"], call.Arguments);
    }

    [Fact]
    public void New_worker_maps_to_the_worker_template()
    {
        var runner = new FakeProcessRunner();
        CliRunner.Run(["new", "worker", "-n", "Billing.Nightly", "--trigger", "schedule"], runner, TextWriter.Null, TextWriter.Null);

        Assert.Equal("goldpath-worker", Assert.Single(runner.Calls).Arguments[1]);
    }

    [Fact]
    public void New_unknown_kind_exits_2_without_running_anything()
    {
        var runner = new FakeProcessRunner();
        Assert.Equal(2, CliRunner.Run(["new", "gateway"], runner, TextWriter.Null, TextWriter.Null));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void New_failure_teaches_the_template_install()
    {
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["goldpath-solution"] = 103;
        var error = new StringWriter();

        Assert.Equal(103, CliRunner.Run(["new", "solution"], runner, TextWriter.Null, error));
        Assert.Contains("dotnet new install Goldpath.Templates", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Check_runs_validate_drift_build_in_that_order()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();

        Assert.Equal(0, CliRunner.Run(["check", "--path", app.Root], runner, TextWriter.Null, TextWriter.Null));

        Assert.Equal(6, runner.Calls.Count);   // validate, drift, tool+package restore + db status, build
        Assert.Contains("validate", runner.Calls[0].Arguments);
        Assert.Contains("--schema", runner.Calls[0].Arguments);
        Assert.Contains("--rules", runner.Calls[0].Arguments);   // the app ships rules — they must ride along
        Assert.Contains("drift", runner.Calls[1].Arguments);
        Assert.Equal(["tool", "restore"], runner.Calls[2].Arguments);
        Assert.Equal(["restore"], runner.Calls[3].Arguments);
        Assert.Contains("has-pending-model-changes", runner.Calls[4].Arguments);
        Assert.Equal(["build", "--nologo"], runner.Calls[5].Arguments);
        Assert.All(runner.Calls, c => Assert.Equal(app.Root, c.WorkingDirectory));
    }

    [Fact]
    public void Check_stops_at_the_first_red_step()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["validate"] = 1;

        Assert.Equal(1, CliRunner.Run(["check", "--path", app.Root], runner, TextWriter.Null, TextWriter.Null));
        Assert.Single(runner.Calls);
    }

    [Fact]
    public void Unexpected_trailing_arguments_exit_2()
    {
        Assert.Equal(2, CliRunner.Run(["check", "--frobnicate"], new FakeProcessRunner(), TextWriter.Null, TextWriter.Null));
    }
}
