namespace Goldpath.Cli;

/// <summary>
/// <c>goldpath init</c> — the L2 attach (foundation §1): a manifest and the schema gate
/// join an EXISTING solution, gradually. Deliberately narrow: it writes the manifest and
/// validates it with the embedded schema — it rewires NO code (brownfield rewiring is
/// the transformation pack's work, template-completion RFC D5; the skills family arrives
/// with it). The manifest is the unit Goldpath binds to — attaching it is the first,
/// honest step of every gradual adoption.
/// </summary>
public static class InitCommand
{
    /// <summary>Attaches the manifest to the solution at <paramref name="appRoot"/>.</summary>
    public static int Run(string appRoot, IPrompter prompter, IProcessRunner runner, TextWriter output, TextWriter error)
    {
        appRoot = Path.GetFullPath(appRoot);
        var manifestDir = Path.Combine(appRoot, ".goldpath");
        var manifestFile = Path.Combine(manifestDir, "manifest.yaml");
        if (File.Exists(manifestFile))
        {
            throw new CliFailureException($"{manifestFile} already exists — this solution is attached; `goldpath check` is the daily verb.");
        }

        if (Directory.GetFiles(appRoot, "*.sln").Length == 0
            && Directory.GetFiles(appRoot, "*.csproj", SearchOption.AllDirectories).Length == 0)
        {
            throw new CliFailureException($"no .sln or .csproj under {appRoot} — goldpath init attaches to an EXISTING solution (a new one starts with goldpath new).");
        }

        var defaultName = AddWorkerCommand.Pascal(Path.GetFileName(appRoot.TrimEnd(Path.DirectorySeparatorChar)));
        var name = Or(prompter.Input($"Solution name (default: {defaultName})"), defaultName);
        var owner = Or(prompter.Input("Owning team (kebab-case, e.g. team-orders)"), "platform-team");
        var description = Or(prompter.Input("One-line description"), $"{name} — attached to the golden path (L2)");

        Directory.CreateDirectory(manifestDir);
        File.WriteAllText(manifestFile, $"""
# Attached by `goldpath init` (L2): the manifest is the single source of truth from here
# on — grow it as capabilities adopt the golden path; `goldpath check` validates it.
# Code rewiring stays YOURS until the transformation pack: init attaches, never rewrites.
schemaVersion: 1
kind: solution
name: {name}
description: {description}
owner: {owner}
""");

        if (SpecdriftGate.Validate(appRoot, runner) != 0)
        {
            File.Delete(manifestFile);
            throw new CliFailureException("the engine rejected the manifest — nothing attached (fix the inputs and retry).");
        }

        output.WriteLine($"── goldpath init: {name} attached (kind: solution, owner: {owner})");
        output.WriteLine("   the manifest is now this solution's single source of truth (ADR-0001);");
        output.WriteLine("   next: declare providers/features AS they adopt the path, `goldpath check` on every change;");
        output.WriteLine("   code rewiring and the skills family arrive with the transformation pack — init attaches, never rewrites.");
        return 0;
    }

    private static string Or(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
