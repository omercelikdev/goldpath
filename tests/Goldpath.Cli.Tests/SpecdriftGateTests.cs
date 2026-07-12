using Xunit;

namespace Goldpath.Cli.Tests;

public class SpecdriftGateTests
{
    [Fact]
    public void Validate_invokes_the_engine_with_manifest_schema_and_rules_in_order()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();

        Assert.Equal(0, SpecdriftGate.Validate(app.Root, runner));

        var call = Assert.Single(runner.Calls);
        Assert.Equal("specdrift", call.FileName);
        Assert.Equal(app.Root, call.WorkingDirectory);
        Assert.Equal("validate", call.Arguments[0]);
        Assert.Equal(Path.Combine(".goldpath", "manifest.yaml"), call.Arguments[1]);
        Assert.Equal("--schema", call.Arguments[2]);
        Assert.True(File.Exists(call.Arguments[3]), "the embedded schema must be materialized on disk");
        Assert.Contains("$schema", File.ReadAllText(call.Arguments[3]), StringComparison.Ordinal);
        Assert.Equal("--rules", call.Arguments[4]);
        Assert.Equal(Path.Combine(".specdrift", "rules.yaml"), call.Arguments[5]);
    }

    [Fact]
    public void Validate_omits_rules_when_the_app_ships_none()
    {
        using var app = new FakeApp();
        File.Delete(Path.Combine(app.Root, ".specdrift/rules.yaml"));
        var runner = new FakeProcessRunner();

        SpecdriftGate.Validate(app.Root, runner);

        Assert.DoesNotContain("--rules", Assert.Single(runner.Calls).Arguments);
    }

    [Fact]
    public void Drift_runs_against_the_app_root()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();

        Assert.Equal(0, SpecdriftGate.Drift(app.Root, runner));

        var call = Assert.Single(runner.Calls);
        Assert.Equal("specdrift", call.FileName);
        Assert.Equal(["drift", "--repo", "."], call.Arguments);
        Assert.Equal(app.Root, call.WorkingDirectory);
    }

    [Fact]
    public void GOLDPATH_SPECDRIFT_override_splits_into_command_and_prefix()
    {
        using var app = new FakeApp();
        var runner = new FakeProcessRunner();
        Environment.SetEnvironmentVariable("GOLDPATH_SPECDRIFT", "dotnet run --project /x/specdrift --");
        try
        {
            SpecdriftGate.Drift(app.Root, runner);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOLDPATH_SPECDRIFT", null);
        }

        var call = Assert.Single(runner.Calls);
        Assert.Equal("dotnet", call.FileName);
        Assert.Equal(["run", "--project", "/x/specdrift", "--", "drift", "--repo", "."], call.Arguments);
    }
}
