namespace Goldpath.Cli;

/// <summary>
/// Verb dispatch (exit codes mirror specdrift: 0 ok, 1 failure/findings, 2 usage). The CLI
/// WRAPS what exists — templates, anchors, the drift profile, the engine — no new semantics.
/// </summary>
public static class CliRunner
{
    private const string Usage = """
        goldpath — the Goldpath golden-path CLI (thin and deterministic)

        usage:
          goldpath new solution|worker [dotnet-new args...]   generate from the golden templates
          goldpath add feature <name> [--path <dir>]          wire a Ring B feature into an existing app
          goldpath add worker <name> [--trigger queue|schedule|jobs] [--path <dir>]
                                                         add a worker project to an existing solution
          goldpath db init|add <name>|status|bundle [--path <dir>]
                                                         the schema lifecycle (wraps dotnet ef, owner-aware)
          goldpath check [--path <dir>]                       specdrift validate + drift + db status + build

        features: multitenancy, audittrail, softdelete, idempotency, dataprotection, caching, locking, archival, bulk, notification, campaign
        """;

    /// <summary>Parses the verb and runs the matching command.</summary>
    public static int Run(string[] args, IProcessRunner runner, TextWriter output, TextWriter error)
    {
        try
        {
            return args switch
            {
                ["new", var kind, .. var rest] => NewCommand.Run(kind, rest, runner, output, error),
                ["add", "feature", var name, .. var rest] => AddFeatureCommand.Run(name, ParsePath(rest), runner, output, error),
                ["add", "worker", var name, .. var rest] => RunAddWorker(name, rest, runner, output, error),
                ["db", var verb, .. var rest] => RunDb(verb, rest, runner, output, error),
                ["check", .. var rest] => CheckCommand.Run(ParsePath(rest), runner, output, error),
                _ => UsageError(error),
            };
        }
        catch (CliUsageException exception)
        {
            error.WriteLine($"goldpath: {exception.Message}");
            return 2;
        }
        catch (CliFailureException exception)
        {
            error.WriteLine($"goldpath: {exception.Message}");
            return 1;
        }
    }

    private static int RunDb(string verb, IReadOnlyList<string> rest, IProcessRunner runner, TextWriter output, TextWriter error)
    {
        // add takes a NAME, bundle takes an optional output dir; both may carry --path.
        string? name = null;
        var remaining = rest;
        if (verb is "add" or "bundle" && rest.Count > 0 && !rest[0].StartsWith("--", StringComparison.Ordinal))
        {
            name = rest[0];
            remaining = [.. rest.Skip(1)];
        }

        return DbCommand.Run(verb, name, ParsePath(remaining), runner, output, error);
    }

    private static int RunAddWorker(string name, IReadOnlyList<string> rest, IProcessRunner runner, TextWriter output, TextWriter error)
    {
        var trigger = "queue";
        var path = ".";
        for (var i = 0; i < rest.Count; i += 2)
        {
            switch (rest[i])
            {
                case "--trigger" when i + 1 < rest.Count:
                    trigger = rest[i + 1];
                    break;
                case "--path" when i + 1 < rest.Count:
                    path = rest[i + 1];
                    break;
                default:
                    throw new CliUsageException($"unexpected arguments: {string.Join(' ', rest)} (only --trigger <t> and --path <dir> are understood here)");
            }
        }

        return AddWorkerCommand.Run(name, trigger, path, runner, output, error);
    }

    private static string ParsePath(IReadOnlyList<string> rest) => rest switch
    {
        [] => ".",
        ["--path", var path] => path,
        _ => throw new CliUsageException($"unexpected arguments: {string.Join(' ', rest)} (only --path <dir> is understood here)"),
    };

    private static int UsageError(TextWriter error)
    {
        error.WriteLine(Usage);
        return 2;
    }
}

/// <summary>Wrong invocation — exits 2 with the message.</summary>
public sealed class CliUsageException(string message) : Exception(message);

/// <summary>A command failed — exits 1 with the message.</summary>
public sealed class CliFailureException(string message) : Exception(message);
