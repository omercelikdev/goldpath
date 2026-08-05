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
    bool generate = true) : IPrompter
{
    public string Choose(string question, IReadOnlyList<string> choices, string defaultChoice)
        => question.StartsWith("Database", StringComparison.Ordinal) ? database
        : question.StartsWith("Authentication", StringComparison.Ordinal) ? auth
        : layout;

    public IReadOnlyList<string> ChooseMany(string question, IReadOnlyList<string> choices) => features ?? [];

    public bool Confirm(string question, bool defaultAnswer)
        => question.StartsWith("Generate", StringComparison.Ordinal) ? generate : outbox;

    public string Input(string question) => name;
}

public class WizardTests
{
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
