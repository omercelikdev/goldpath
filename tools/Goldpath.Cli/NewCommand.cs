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
            var serviceName = FirstBareToken(rest)
                ?? throw new CliUsageException("goldpath new service needs a name: goldpath new service <Name>");
            return NewServiceCommand.RunService(serviceName, FindFlagValue(rest, "--path") ?? Directory.GetCurrentDirectory(), runner, output, error);
        }

        if (kind is "gateway")
        {
            return NewServiceCommand.RunGateway(FindFlagValue(rest, "--path") ?? Directory.GetCurrentDirectory(), runner, output, error);
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
        => ResolveAppRoot(rest, Directory.GetCurrentDirectory());

    /// <summary>
    /// Where the generated app lives: <c>-o</c> when given; otherwise the template's
    /// <c>preferNameDirectory</c> put it under <c>&lt;cwd&gt;/&lt;name&gt;</c> — resolve THAT when
    /// it exists, so the post-generation db init and the first-contract commit find the
    /// app instead of silently skipping it (they searched <c>&lt;cwd&gt;/src</c> before).
    /// </summary>
    public static string ResolveAppRoot(IReadOnlyList<string> rest, string currentDirectory)
    {
        if (FindFlagValue(rest, "-o", "--output") is { } explicitOutput)
        {
            return explicitOutput;
        }

        if (FindFlagValue(rest, "-n", "--name") is { } name && Directory.Exists(Path.Combine(currentDirectory, name)))
        {
            return Path.Combine(currentDirectory, name);
        }

        return currentDirectory;
    }

    /// <summary>The value following the first matching flag, or null.</summary>
    private static string? FindFlagValue(IReadOnlyList<string> rest, params string[] flags)
    {
        for (var i = 0; i < rest.Count - 1; i++)
        {
            if (flags.Contains(rest[i], StringComparer.Ordinal))
            {
                return rest[i + 1];
            }
        }

        return null;
    }

    /// <summary>
    /// The first token that is neither a flag nor a flag's VALUE — so
    /// `new service --path /app Billing` names Billing, whatever the order (review R3).
    /// </summary>
    private static string? FirstBareToken(IReadOnlyList<string> rest)
    {
        for (var i = 0; i < rest.Count; i++)
        {
            if (rest[i].StartsWith("-", StringComparison.Ordinal))
            {
                i++;   // its value rides with it
                continue;
            }

            return rest[i];
        }

        return null;
    }
}
