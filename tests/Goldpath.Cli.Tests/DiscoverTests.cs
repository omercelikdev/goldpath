using Xunit;

namespace Goldpath.Cli.Tests;

public class DiscoverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"goldpath-discover-{Guid.NewGuid():N}");

    public DiscoverTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Manifest(string relativeSolutionDir, string body)
    {
        var dir = Path.Combine(_root, relativeSolutionDir, ".goldpath");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.yaml"), body);
    }

    private (int Exit, string Out) Run()
    {
        var output = new StringWriter();
        var exit = CliRunner.Run(["discover", "--path", _root], new FakeProcessRunner(), output, TextWriter.Null);
        return (exit, output.ToString());
    }

    [Fact]
    public void Discover_inventories_every_manifest_with_what_it_declares()
    {
        Manifest("apps/orders", "kind: solution\nname: OrderPlatform\n");
        Manifest("apps/billing", "kind: solution\nname: Billing\nproducts:\n  - name: qorpe.apiPortal\n    enabled: true\n");

        var (exit, text) = Run();

        Assert.Equal(0, exit);
        Assert.Contains("kind=solution", text, StringComparison.Ordinal);
        Assert.Contains("name=OrderPlatform", text, StringComparison.Ordinal);
        // ADR-0012's namespaced products are the point of the inventory: a product must be
        // visible from the tree without opening a single repo.
        Assert.Contains("products=qorpe.apiPortal", text, StringComparison.Ordinal);
        Assert.Contains("2 manifest(s)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_tree_is_an_ANSWER_not_a_failure()
    {
        var (exit, text) = Run();

        Assert.Equal(0, exit);
        Assert.Contains("no Goldpath manifests", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Vendor_and_build_trees_are_never_walked()
    {
        // A manifest inside node_modules belongs to somebody else's package; inventorying it
        // would report a dependency as if it were ours (and cost a tree walk to do it).
        Manifest(Path.Combine("ui", "node_modules", "someone-else"), "kind: solution\nname: NotOurs\n");
        Manifest(Path.Combine("src", "api", "bin", "Debug"), "kind: solution\nname: BuildOutput\n");
        Manifest("keeper", "kind: solution\nname: Keeper\n");

        var (exit, text) = Run();

        Assert.Equal(0, exit);
        Assert.Contains("name=Keeper", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NotOurs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildOutput", text, StringComparison.Ordinal);
        Assert.Contains("1 manifest(s)", text, StringComparison.Ordinal);
    }
}

public class HelpAndVersionTests
{
    [Fact]
    public void Help_is_an_answer_on_stdout_with_exit_zero()
    {
        foreach (string[] args in new[] { new[] { "--help" }, ["-h"], ["help"] })
        {
            var output = new StringWriter();
            var error = new StringWriter();

            var exit = CliRunner.Run(args, new FakeProcessRunner(), output, error);

            // The bug this pins: `goldpath --help` used to fall through to the usage ERROR arm —
            // stderr, exit 2 — which is the first thing a freshly-installed tool's user types.
            Assert.Equal(0, exit);
            Assert.Contains("usage:", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("goldpath discover", output.ToString(), StringComparison.Ordinal);
            Assert.Empty(error.ToString());
        }
    }

    [Fact]
    public void Version_reports_the_assembly_the_user_installed()
    {
        var output = new StringWriter();

        var exit = CliRunner.Run(["--version"], new FakeProcessRunner(), output, TextWriter.Null);

        Assert.Equal(0, exit);
        Assert.Matches(@"^\d+\.\d+\.\d+", output.ToString().Trim());
    }

    [Fact]
    public void An_unknown_verb_is_still_a_usage_ERROR()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = CliRunner.Run(["frobnicate"], new FakeProcessRunner(), output, error);

        Assert.Equal(2, exit);
        Assert.Contains("usage:", error.ToString(), StringComparison.Ordinal);
    }
}
