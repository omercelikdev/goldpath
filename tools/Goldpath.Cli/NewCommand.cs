namespace Goldpath.Cli;

/// <summary>
/// <c>goldpath new solution|worker ...</c> — a strict passthrough to <c>dotnet new</c> with the
/// golden template names, so teams learn ONE entry point. All template arguments
/// (<c>--features</c>, <c>--trigger</c>, <c>--db</c>...) flow through untouched.
/// </summary>
public static class NewCommand
{
    /// <summary>Maps the kind to its template and delegates to dotnet new.</summary>
    public static int Run(string kind, IReadOnlyList<string> rest, IProcessRunner runner, TextWriter output, TextWriter error)
    {
        if (kind is "service")
        {
            var serviceName = rest.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
                ?? throw new CliUsageException("goldpath new service needs a name: goldpath new service <Name>");
            return NewServiceCommand.RunService(serviceName, ParseNewPath(rest), runner, output, error);
        }

        if (kind is "gateway")
        {
            return NewServiceCommand.RunGateway(ParseNewPath(rest), runner, output, error);
        }

        var template = kind switch
        {
            "solution" => "goldpath-solution",
            "worker" => "goldpath-worker",
            _ => throw new CliUsageException($"unknown kind '{kind}' — goldpath new solution|worker|service|gateway"),
        };

        var exitCode = runner.Run("dotnet", ["new", template, .. rest], Directory.GetCurrentDirectory());
        if (exitCode != 0)
        {
            error.WriteLine("goldpath: generation failed — if the template is missing: dotnet new install Goldpath.Templates");
            return exitCode;
        }

        // Migrations RFC D2: the template cannot carry one Initial migration per feature
        // combination — the CLI generates it against the just-composed model. Best effort:
        // a feed-less restore may legitimately fail here; generation still succeeded.
        var appRoot = OutputDirectory(rest);
        try
        {
            // db init owns the first-contract commit too (#32) — it owns the build moment.
            DbCommand.Run("init", null, appRoot, runner, output, error);
        }
        catch (CliFailureException exception)
        {
            output.WriteLine($"goldpath: generated, but the Initial migration is pending ({exception.Message})");
            output.WriteLine("goldpath: wire your package feed (nuget.config), then run: goldpath db init");
        }

        return 0;
    }

    private static string OutputDirectory(IReadOnlyList<string> rest)
    {
        for (var i = 0; i < rest.Count - 1; i++)
        {
            if (rest[i] is "-o" or "--output")
            {
                return rest[i + 1];
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static string ParseNewPath(IReadOnlyList<string> rest)
    {
        for (var i = 0; i < rest.Count - 1; i++)
        {
            if (rest[i] == "--path")
            {
                return rest[i + 1];
            }
        }

        return Directory.GetCurrentDirectory();
    }
}
