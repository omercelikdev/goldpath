using Xunit;

namespace Goldpath.Cli.Tests;

/// <summary>Scripted prompter: answers in order, records nothing it wasn't asked.</summary>
public sealed class FakePrompter(
    string name,
    string database = "postgresql",
    string auth = "openid",
    string layout = "vertical-slice",
    IReadOnlyList<string>? features = null,
    bool outbox = false,
    bool generate = true,
    string kind = "solution",
    string trigger = "queue") : IPrompter
{
    public string Choose(string question, IReadOnlyList<string> choices, string defaultChoice)
        => question.StartsWith("What are we building", StringComparison.Ordinal) ? kind
        : question.StartsWith("Worker trigger", StringComparison.Ordinal) ? trigger
        : question.StartsWith("Database", StringComparison.Ordinal) ? database
        : question.StartsWith("Authentication", StringComparison.Ordinal) ? auth
        : layout;

    public IReadOnlyList<string> ChooseMany(string question, IReadOnlyList<string> choices) => features ?? [];

    public bool Confirm(string question, bool defaultAnswer)
        => question.StartsWith("Generate", StringComparison.Ordinal) ? generate : outbox;

    public string Input(string question) => name;
}

public class WizardTests
{
    [Fact]
    public void The_wizard_can_birth_a_worker_through_the_same_new_verb()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();

        var exit = WizardCommand.Run(new FakePrompter("Billing.Nightly", kind: "worker", trigger: "schedule", database: "sqlserver"), runner, output, TextWriter.Null);

        Assert.Equal(0, exit);
        Assert.Equal(["new", "goldpath-worker", "-n", "Billing.Nightly", "--trigger", "schedule", "--db", "sqlserver"], runner.Calls[0].Arguments);
        Assert.Contains("equivalent command: goldpath new worker -n Billing.Nightly --trigger schedule --db sqlserver", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("trigger: schedule", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Declining_the_worker_generates_nothing()
    {
        var runner = new FakeProcessRunner();
        Assert.Equal(0, WizardCommand.Run(new FakePrompter("W", kind: "worker", generate: false), runner, new StringWriter(), TextWriter.Null));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void The_app_root_follows_the_name_directory_when_o_is_omitted()
    {
        var cwd = Directory.CreateTempSubdirectory("goldpath-root-").FullName;
        try
        {
            Assert.Equal("/explicit", NewCommand.ResolveAppRoot(["-n", "Shop", "-o", "/explicit"], cwd));
            Assert.Equal(cwd, NewCommand.ResolveAppRoot(["-n", "Shop"], cwd));   // nothing generated yet → cwd
            Directory.CreateDirectory(Path.Combine(cwd, "Shop"));
            Assert.Equal(Path.Combine(cwd, "Shop"), NewCommand.ResolveAppRoot(["-n", "Shop"], cwd));   // preferNameDirectory's home
        }
        finally
        {
            Directory.Delete(cwd, recursive: true);
        }
    }

    private static WizardCommand.Plan Derive(IReadOnlyList<string>? features = null, bool outbox = false,
        string db = "postgresql", string auth = "openid", string layout = "vertical-slice")
        => WizardCommand.Derive(new WizardCommand.Answers("Shop", db, auth, layout, features ?? [], outbox));

    [Fact]
    public void Nothing_broker_shaped_means_no_broker_with_the_reason_said()
    {
        var plan = Derive(features: ["bulk", "audittrail"]);
        Assert.Contains("--broker", plan.Arguments);
        Assert.Contains("none", plan.Arguments);
        Assert.Contains(plan.Notes, n => n.StartsWith("no broker needed — removed", StringComparison.Ordinal));
        Assert.Contains(plan.Notes, n => n.Contains("jobs scheduler", StringComparison.Ordinal) && n.Contains("bulk", StringComparison.Ordinal));
        Assert.Contains(plan.Notes, n => n.StartsWith("no Redis — removed", StringComparison.Ordinal));
    }

    [Fact]
    public void Campaign_mandates_the_broker_and_says_why()
    {
        var plan = Derive(features: ["campaign"]);
        Assert.DoesNotContain("--broker", plan.Arguments);   // rabbitmq is the template default
        Assert.Contains(plan.Notes, n => n.Contains("campaign REQUIRES one", StringComparison.Ordinal));
    }

    [Fact]
    public void The_outbox_alone_keeps_the_broker()
    {
        var plan = Derive(outbox: true);
        Assert.DoesNotContain("--broker", plan.Arguments);
        Assert.Contains(plan.Notes, n => n.Contains("outbox publishes THROUGH a broker", StringComparison.Ordinal));
    }

    [Fact]
    public void Caching_is_the_only_redis_source_and_idempotency_learns_its_fallback()
    {
        Assert.Contains(Derive(features: ["caching"]).Notes, n => n.StartsWith("redis joins", StringComparison.Ordinal));
        var withoutCaching = Derive(features: ["idempotency"]);
        Assert.Contains(withoutCaching.Notes, n => n.Contains("memory cache", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_chosen_module_travels_as_a_features_argument()
    {
        var plan = Derive(features: ["multitenancy", "softdelete"], layout: "clean-architecture");
        Assert.Equal(2, plan.Arguments.Count(a => a == "--features"));
        Assert.Contains("multitenancy", plan.Arguments);
        Assert.Contains("clean-architecture", plan.Arguments);
    }

    [Fact]
    public void Unknown_module_names_never_reach_the_generator()
    {
        var plan = Derive(features: ["bulk", "blockchain"]);
        Assert.DoesNotContain("blockchain", plan.Arguments);
    }

    [Fact]
    public void The_wizard_delegates_to_the_same_generator_flow()
    {
        var runner = new FakeProcessRunner();
        var exit = WizardCommand.Run(new FakePrompter("Shop", features: ["bulk"]), runner, TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, exit);
        var call = Assert.Single(runner.Calls, c => c.Arguments.Contains("goldpath-solution"));
        Assert.Contains("-n", call.Arguments);
        Assert.Contains("Shop", call.Arguments);
        Assert.Contains("--features", call.Arguments);
        Assert.Contains("bulk", call.Arguments);
    }

    [Fact]
    public void Declining_the_confirm_generates_nothing()
    {
        var runner = new FakeProcessRunner();
        Assert.Equal(0, WizardCommand.Run(new FakePrompter("Shop", generate: false), runner, TextWriter.Null, TextWriter.Null));
        Assert.Empty(runner.Calls);
    }
}

public class InitTests
{
    private sealed class SilentPrompter : IPrompter
    {
        public string Choose(string question, IReadOnlyList<string> choices, string defaultChoice) => defaultChoice;
        public IReadOnlyList<string> ChooseMany(string question, IReadOnlyList<string> choices) => [];
        public bool Confirm(string question, bool defaultAnswer) => defaultAnswer;
        public string Input(string question) => "";
    }

    private static int Init(string root, FakeProcessRunner runner)
        => InitCommand.Run(root, new SilentPrompter(), runner, TextWriter.Null, TextWriter.Null);

    private static string TempSolution()
    {
        var root = Path.Combine(Path.GetTempPath(), $"goldpath-init-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Legacy.sln"), "");
        return root;
    }

    [Fact]
    public void Init_attaches_a_valid_manifest_to_an_existing_solution()
    {
        var root = TempSolution();
        try
        {
            var runner = new FakeProcessRunner();
            Assert.Equal(0, Init(root, runner));
            var manifest = File.ReadAllText(Path.Combine(root, ".goldpath", "manifest.yaml"));
            Assert.Contains("kind: solution", manifest, StringComparison.Ordinal);
            Assert.Contains("init attaches, never rewrites", manifest, StringComparison.Ordinal);
            Assert.Contains(runner.Calls, c => c.Arguments.Contains("validate"));   // the engine gated it
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Init_refuses_an_already_attached_solution_and_an_empty_directory()
    {
        var root = TempSolution();
        try
        {
            Assert.Equal(0, Init(root, new FakeProcessRunner()));
            Assert.Throws<CliFailureException>(() => Init(root, new FakeProcessRunner()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        var empty = Path.Combine(Path.GetTempPath(), $"goldpath-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);
        try
        {
            Assert.Throws<CliFailureException>(() => Init(empty, new FakeProcessRunner()));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void A_rejected_manifest_leaves_nothing_behind()
    {
        var root = TempSolution();
        try
        {
            var runner = new FakeProcessRunner();
            runner.ExitCodeWhenArgumentsContain["validate"] = 1;
            Assert.Throws<CliFailureException>(() => Init(root, runner));
            Assert.False(File.Exists(Path.Combine(root, ".goldpath", "manifest.yaml")));
            Assert.False(Directory.Exists(Path.Combine(root, ".goldpath")));   // the DIRECTORY too (review R3)
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
