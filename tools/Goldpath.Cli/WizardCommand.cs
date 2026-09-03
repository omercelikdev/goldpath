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
    /// <summary>
    /// The module menu — the CANONICAL feature list `goldpath add feature` understands
    /// (review R6: one list, referenced, so the menu and the unknown-module filter can
    /// never desync from what the recipes actually wire).
    /// </summary>
    public static IReadOnlyList<string> Modules => FeatureRecipes.Names;

    /// <summary>
    /// The WORKER's feature menu — the features whose concept exists in a process without
    /// business HTTP (open-threads T25: concept parity, not full parity). layout, idempotency,
    /// caching, archival, bulk, campaign and approvals are solution-shaped and not offered.
    /// </summary>
    public static IReadOnlyList<string> WorkerModules { get; } =
        ["multitenancy", "audittrail", "softdelete", "dataprotection", "locking", "notification", "fileexchange"];

    /// <summary>The worker features that own tables — impossible on a schedule worker, which has no database.</summary>
    public static IReadOnlyList<string> WorkerTableOwners { get; } =
        ["audittrail", "softdelete", "locking", "notification", "fileexchange"];

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
        // The name comes FIRST (a blank one is a usage error before any other question —
        // the pinned contract), then the kind: the solution (a web head, the walking
        // skeleton) or the worker (a process with a trigger and no HTTP surface). Service
        // and gateway heads are grown INTO a solution afterwards, never born here.
        var name = prompter.Input("Name (e.g. OrderPlatform, or Billing.Nightly for a worker)");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CliUsageException("the wizard needs a name.");
        }

        var kind = prompter.Choose("What are we building?", ["solution", "worker"], "solution");
        if (kind == "worker")
        {
            var trigger = prompter.Choose("Worker trigger", ["queue", "schedule", "jobs"], "queue");
            var workerDb = prompter.Choose("Database", ["postgresql", "sqlserver"], "postgresql");
            // The management head's floor: a worker is internal by default, so none is the
            // default here (the solution defaults to openid because it serves business APIs).
            var workerAuth = prompter.Choose("Authentication (the management head: admin surfaces + console)", ["openid", "apikey", "none"], "none");
            var workerFeatures = prompter.ChooseMany("Which features does this worker need?", WorkerModules)
                .Where(WorkerModules.Contains).Distinct(StringComparer.Ordinal).ToList();
            var tableOwners = workerFeatures.Where(WorkerTableOwners.Contains).ToList();
            if (trigger == "schedule" && tableOwners.Count > 0)
            {
                throw new CliUsageException(
                    $"{string.Join(", ", tableOwners)} own tables in the worker's database, and a schedule worker (PeriodicTimer) has none — pick the queue or jobs trigger, or drop them.");
            }

            output.WriteLine();
            output.WriteLine("── the derived shape:");
            output.WriteLine(trigger switch
            {
                "queue" => "   trigger: queue — a broker consumer with the outbox/inbox tables in the worker's database",
                "schedule" => "   trigger: schedule — a PeriodicTimer batch; no scheduler store, so `goldpath db` has no owner here",
                _ => "   trigger: jobs — the Goldpath jobs scheduler with its admin surface, runs in the worker's database",
            });
            output.WriteLine($"   database: {workerDb}");
            output.WriteLine($"   auth: {workerAuth}" + (workerAuth == "none" ? " — admin surfaces opt out VISIBLY; acceptable only behind an authenticating boundary" : " — the admin surfaces and the console sit behind the ops floor"));
            if (workerFeatures.Count > 0)
            {
                output.WriteLine($"   features: {string.Join(", ", workerFeatures)}");
                if (trigger == "queue" && workerFeatures.Any(f => f is "notification" or "fileexchange"))
                {
                    output.WriteLine("   jobs runtime: joins the worker's database next to the inbox — notification/fileexchange ride it, and the console comes with it");
                }
            }

            var workerArguments = new List<string> { "--trigger", trigger, "--db", workerDb, "--auth", workerAuth };
            foreach (var feature in workerFeatures)
            {
                workerArguments.AddRange(["--features", feature]);
            }

            output.WriteLine();
            output.WriteLine($"   equivalent command: goldpath new worker -n {name.Trim()} {string.Join(' ', workerArguments)}");
            if (!prompter.Confirm("Generate?", defaultAnswer: true))
            {
                output.WriteLine("── nothing generated.");
                return 0;
            }

            return NewCommand.Run("worker", ["-n", name.Trim(), .. workerArguments], runner, output, error);
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
    /// broker reason; caching is the ONLY Redis source; the jobs riders (archival, bulk,
    /// notification, campaign, approvals, fileexchange) ride the app database and bring
    /// the operations console.
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

        var jobsRiders = features.Where(f => f is "archival" or "bulk" or "notification" or "campaign" or "approvals" or "fileexchange").ToList();
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
