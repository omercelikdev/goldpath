namespace Goldpath.Cli;

/// <summary>
/// <c>goldpath new</c> with no arguments — the interactive wizard. It asks WHAT the app
/// does (which modules, which auth, which layout) and derives the INFRASTRUCTURE itself,
/// saying why each piece joins or goes ("no broker needed — removed"). The derivation is
/// a pure function (<see cref="Derive"/>) the tests pin; the prompt loop is a thin shell
/// over <see cref="IPrompter"/>. The wizard never generates by a private path: it prints
/// the equivalent command and delegates to the same <c>goldpath new solution</c> flow.
/// </summary>
public static class WizardCommand
{
    /// <summary>The module menu — the eleven features, in the template's vocabulary.</summary>
    public static readonly IReadOnlyList<string> Modules =
    [
        "multitenancy", "audittrail", "softdelete", "idempotency", "dataprotection",
        "caching", "locking", "archival", "bulk", "notification", "campaign",
    ];

    /// <summary>The wizard's answers — one record, so the derivation stays a pure function.</summary>
    public sealed record Answers(
        string Name,
        string Database,
        string Auth,
        string Layout,
        IReadOnlyList<string> Features,
        bool PublishesIntegrationEvents);

    /// <summary>The derived plan: the generator arguments plus the WHY of every infrastructure decision.</summary>
    public sealed record Plan(IReadOnlyList<string> Arguments, IReadOnlyList<string> Notes);

    /// <summary>Runs the interactive flow and delegates to <c>goldpath new solution</c>.</summary>
    public static int Run(IPrompter prompter, IProcessRunner runner, TextWriter output, TextWriter error)
    {
        output.WriteLine("── goldpath new (wizard): say what the app DOES — the infrastructure is derived, with reasons.");
        var name = prompter.Input("Solution name (e.g. OrderPlatform)");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CliUsageException("the wizard needs a solution name.");
        }

        var answers = new Answers(
            name.Trim(),
            prompter.Choose("Database", ["postgresql", "sqlserver"], "postgresql"),
            prompter.Choose("Authentication", ["openid", "apikey", "none"], "openid"),
            prompter.Choose("Code layout", ["vertical-slice", "clean-architecture"], "vertical-slice"),
            prompter.ChooseMany("Which modules does this app need?", Modules),
            prompter.Confirm("Will it publish integration events to other systems (outbox)?", defaultAnswer: false));

        var plan = Derive(answers);
        output.WriteLine();
        output.WriteLine("── the derived shape:");
        foreach (var note in plan.Notes)
        {
            output.WriteLine($"   {note}");
        }

        output.WriteLine();
        output.WriteLine($"   equivalent command: goldpath new solution -n {answers.Name} {string.Join(' ', plan.Arguments)}");
        if (!prompter.Confirm("Generate?", defaultAnswer: true))
        {
            output.WriteLine("── nothing generated.");
            return 0;
        }

        return NewCommand.Run("solution", ["-n", answers.Name, .. plan.Arguments], runner, output, error);
    }

    /// <summary>
    /// The derivation table, verified against what the recipes actually wire: campaign is
    /// the only module that MANDATES a broker (RFC D8) and outbox is the only other
    /// broker reason; caching is the ONLY Redis source; the jobs quartet rides the app
    /// database and brings the operations console.
    /// </summary>
    public static Plan Derive(Answers answers)
    {
        var arguments = new List<string> { "--db", answers.Database, "--auth", answers.Auth };
        var notes = new List<string>
        {
            $"database: {answers.Database} — every shape owns one",
            $"auth: {answers.Auth}" + (answers.Auth == "none" ? " — admin surfaces opt out VISIBLY; acceptable only behind an authenticating boundary" : ""),
        };

        if (answers.Layout != "vertical-slice")
        {
            arguments.AddRange(["--layout", answers.Layout]);
        }

        notes.Add($"layout: {answers.Layout}");

        var features = answers.Features.Where(Modules.Contains).Distinct(StringComparer.Ordinal).ToList();
        var wantsBroker = answers.PublishesIntegrationEvents || features.Contains("campaign");
        if (wantsBroker)
        {
            notes.Add(features.Contains("campaign")
                ? "broker: rabbitmq — campaign REQUIRES one (the release path IS broker fan-out, RFC D8)"
                : "broker: rabbitmq — the outbox publishes THROUGH a broker");
        }
        else
        {
            arguments.AddRange(["--broker", "none"]);
            notes.Add("no broker needed — removed (nothing you chose publishes through one)");
        }

        if (features.Contains("caching"))
        {
            notes.Add("redis joins — caching is its only source (HybridCache L1+L2)");
        }
        else
        {
            notes.Add("no Redis — removed (only the caching module brings it)");
            if (features.Contains("idempotency"))
            {
                notes.Add("idempotency stores keys in a memory cache — enable caching for Redis-backed keys");
            }
        }

        var jobsRiders = features.Where(f => f is "archival" or "bulk" or "notification" or "campaign").ToList();
        if (jobsRiders.Count > 0)
        {
            notes.Add($"jobs scheduler + the operations console ride the app database ({string.Join(", ", jobsRiders)})");
        }

        foreach (var feature in features)
        {
            arguments.AddRange(["--features", feature]);
        }

        notes.Add(features.Count > 0
            ? $"modules: {string.Join(", ", features)}"
            : "modules: none — the walking skeleton only");
        return new Plan(arguments, notes);
    }
}
