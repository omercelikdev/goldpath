namespace Goldpath.Cli;

/// <summary>
/// <c>goldpath db init|add|status|bundle</c> — the schema lifecycle verbs (migrations RFC D5).
/// Thin over <c>dotnet ef</c> (the CLI never re-implements what the tool owns), but
/// multi-owner aware: every project referencing the EF Design package OWNS migrations for
/// its context, and each verb fans out across the owners so a multi-head solution
/// (api + workers) stays one command. Shared package tables have ONE owner (RFC D3);
/// non-owners map them with <c>excludeFromMigrations: true</c>.
/// </summary>
public static class DbCommand
{
    /// <summary>Dispatches one db verb.</summary>
    public static int Run(string verb, string? name, string appRoot, IProcessRunner runner, TextWriter output, TextWriter error)
    {
        var owners = FindMigrationOwners(appRoot);
        if (owners.Count == 0)
        {
            // init/status are lifecycle no-ops on an ownerless app (e.g. a schedule-only
            // worker has no database); add/bundle EXPECT an owner and teach.
            if (verb is "init" or "status")
            {
                output.WriteLine("── goldpath db: no migration owner (no project references Microsoft.EntityFrameworkCore.Design) — nothing to do.");
                return 0;
            }

            throw new CliFailureException(
                "no migration owner found — a project owns migrations by referencing Microsoft.EntityFrameworkCore.Design; regenerate from a current template or add the reference to the project that owns the schema.");
        }

        var restore = runner.Run("dotnet", ["tool", "restore"], appRoot);
        if (restore != 0)
        {
            throw new CliFailureException("dotnet tool restore failed — the pinned dotnet-ef tool comes from .config/dotnet-tools.json; see the output above.");
        }

        // `dotnet ef` builds but never restores: a fresh generation has no assets yet.
        if (runner.Run("dotnet", ["restore"], appRoot) != 0)
        {
            throw new CliFailureException("dotnet restore failed — wire your package feed (nuget.config), then re-run.");
        }

        return verb switch
        {
            "init" => Init(owners, appRoot, runner, output),
            "add" => Add(name ?? throw new CliUsageException("goldpath db add needs a name: goldpath db add <migration-name>"), owners, appRoot, runner, output),
            "status" => Status(owners, appRoot, runner, output, error),
            "bundle" => Bundle(name, owners, appRoot, runner, output),
            _ => throw new CliUsageException($"unknown db verb '{verb}' — one of: init, add, status, bundle"),
        };
    }

    /// <summary>The post-generation step (RFC D2): the Initial migration per owner, idempotent.</summary>
    private static int Init(IReadOnlyList<string> owners, string appRoot, IProcessRunner runner, TextWriter output)
    {
        foreach (var owner in owners)
        {
            if (Directory.Exists(Path.Combine(Path.GetDirectoryName(owner)!, "Migrations")))
            {
                output.WriteLine($"── goldpath db init: {Rel(appRoot, owner)} already has Migrations/ — skipped");
                continue;
            }

            output.WriteLine($"── goldpath db init: Initial migration for {Rel(appRoot, owner)}");
            var exitCode = Ef(runner, appRoot, owner, ["migrations", "add", "Initial"]);
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        output.WriteLine("── goldpath db init: done — Development now migrates from these; production applies the bundle");
        return 0;
    }

    private static int Add(string name, IReadOnlyList<string> owners, string appRoot, IProcessRunner runner, TextWriter output)
    {
        foreach (var owner in owners)
        {
            output.WriteLine($"── goldpath db add: '{name}' for {Rel(appRoot, owner)}");
            var exitCode = Ef(runner, appRoot, owner, ["migrations", "add", name]);
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        return 0;
    }

    /// <summary>"The model changed but no migration captures it" — also runs inside goldpath check.</summary>
    public static int Status(IReadOnlyList<string> owners, string appRoot, IProcessRunner runner, TextWriter output, TextWriter error)
    {
        var pending = new List<string>();
        foreach (var owner in owners)
        {
            if (Ef(runner, appRoot, owner, ["migrations", "has-pending-model-changes"]) != 0)
            {
                pending.Add(Rel(appRoot, owner));
            }
        }

        if (pending.Count > 0)
        {
            error.WriteLine($"goldpath db status: the model changed but no migration captures it in: {string.Join(", ", pending)} — run goldpath db add <name>.");
            return 1;
        }

        output.WriteLine("── goldpath db status: every owner's migrations match its model");
        return 0;
    }

    /// <summary>What CI runs (RFC D4); also the air-gapped delivery path.</summary>
    private static int Bundle(string? outputDir, IReadOnlyList<string> owners, string appRoot, IProcessRunner runner, TextWriter output)
    {
        var target = outputDir ?? Path.Combine(appRoot, "artifacts", "migrations");
        foreach (var owner in owners)
        {
            var ownerName = Path.GetFileNameWithoutExtension(owner);
            output.WriteLine($"── goldpath db bundle: {ownerName}");
            var exitCode = Ef(runner, appRoot, owner,
                ["migrations", "bundle", "--force", "--output", Path.Combine(target, $"{ownerName}-migrations")]);
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        output.WriteLine($"── goldpath db bundle: artifacts in {target} — deployment runs these BEFORE the new app version starts (never the app process)");
        return 0;
    }

    /// <summary>Runs status against every owner (the goldpath check hook).</summary>
    public static int StatusForCheck(string appRoot, IProcessRunner runner, TextWriter output, TextWriter error)
    {
        var owners = FindMigrationOwners(appRoot);
        if (owners.Count == 0)
        {
            return 0;   // nothing owns migrations (e.g. a pure gateway) — nothing to check
        }

        if (runner.Run("dotnet", ["tool", "restore"], appRoot) != 0)
        {
            error.WriteLine("goldpath check: dotnet tool restore failed — the pinned dotnet-ef tool comes from .config/dotnet-tools.json.");
            return 1;
        }

        if (runner.Run("dotnet", ["restore"], appRoot) != 0)
        {
            error.WriteLine("goldpath check: dotnet restore failed — wire your package feed (nuget.config).");
            return 1;
        }

        return Status(owners, appRoot, runner, output, error);
    }

    private static int Ef(IProcessRunner runner, string appRoot, string ownerProject, IReadOnlyList<string> args)
        => runner.Run("dotnet", ["ef", .. args, "--project", ownerProject, "--startup-project", ownerProject], appRoot);

    /// <summary>A project OWNS migrations iff it references the EF Design package.</summary>
    internal static IReadOnlyList<string> FindMigrationOwners(string appRoot)
        => [.. Directory.EnumerateFiles(appRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("Microsoft.EntityFrameworkCore.Design", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)];

    private static string Rel(string appRoot, string path)
        => Path.GetRelativePath(appRoot, path);
}
