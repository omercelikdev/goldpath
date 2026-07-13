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
        var template = kind switch
        {
            "solution" => "goldpath-solution",
            "worker" => "goldpath-worker",
            _ => throw new CliUsageException($"unknown kind '{kind}' — goldpath new solution|worker"),
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
            DbCommand.Run("init", null, appRoot, runner, output, error);

            // The first contract commit (SPEC0211): db init just built the app, so the
            // build-time OpenAPI export exists — copy it into specs/ so the very first
            // `goldpath add feature` passes its engine round-trip. The template cannot
            // carry this file statically: the document varies by feature combination.
            CommitFirstContract(appRoot, output);
        }
        catch (CliFailureException exception)
        {
            output.WriteLine($"goldpath: generated, but the Initial migration is pending ({exception.Message})");
            output.WriteLine("goldpath: wire your package feed (nuget.config), then run: goldpath db init");
        }

        return 0;
    }

    private static void CommitFirstContract(string appRoot, TextWriter output)
    {
        var src = Path.Combine(appRoot, "src");
        if (!Directory.Exists(src))
        {
            return;   // not an app layout at all (bare CWD generation)
        }

        // Worker apps pass this point too — they no-op naturally below, because no
        // project of theirs has an openapi/ export directory (no HTTP contract).

        var specs = Path.Combine(appRoot, "specs");
        foreach (var document in Directory.GetDirectories(src)
                     .Select(project => Path.Combine(project, "openapi"))
                     .Where(Directory.Exists)
                     .SelectMany(dir => Directory.GetFiles(dir, "*.json")))
        {
            var target = Path.Combine(specs, Path.GetFileName(document));
            if (File.Exists(target))
            {
                continue;   // never clobber a deliberately edited contract
            }

            Directory.CreateDirectory(specs);
            File.Copy(document, target);
            output.WriteLine($"goldpath: first OpenAPI contract committed to specs/{Path.GetFileName(document)}");
        }
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
}
