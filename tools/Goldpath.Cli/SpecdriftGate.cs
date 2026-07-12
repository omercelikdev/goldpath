using System.Reflection;

namespace Goldpath.Cli;

/// <summary>
/// Runs the deterministic engine against an app: validate (schema embedded in this CLI, so
/// tool and manifest contract cannot drift apart) and drift. The CLI never re-implements a
/// check the engine owns (ADR-0004 discipline).
/// </summary>
public static class SpecdriftGate
{
    /// <summary>Runs <c>specdrift validate</c> with the embedded schema (+ app rules when present).</summary>
    public static int Validate(string appRoot, IProcessRunner runner)
    {
        var (fileName, prefix) = ResolveEngine();
        List<string> arguments =
        [
            .. prefix,
            "validate", Path.Combine(".goldpath", "manifest.yaml"),
            "--schema", ExtractEmbeddedSchema(),
        ];
        var rules = Path.Combine(appRoot, ".specdrift", "rules.yaml");
        if (File.Exists(rules))
        {
            arguments.AddRange(["--rules", Path.Combine(".specdrift", "rules.yaml")]);
        }

        return runner.Run(fileName, arguments, appRoot);
    }

    /// <summary>Runs <c>specdrift drift</c> against the app root.</summary>
    public static int Drift(string appRoot, IProcessRunner runner)
    {
        var (fileName, prefix) = ResolveEngine();
        return runner.Run(fileName, [.. prefix, "drift", "--repo", "."], appRoot);
    }

    // GOLDPATH_SPECDRIFT lets tests and dev loops point at a checkout ("dotnet run --project ... --").
    private static (string FileName, string[] Prefix) ResolveEngine()
    {
        var overrideCommand = Environment.GetEnvironmentVariable("GOLDPATH_SPECDRIFT");
        if (string.IsNullOrWhiteSpace(overrideCommand))
        {
            return ("specdrift", []);
        }

        var parts = overrideCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts[1..]);
    }

    private static string ExtractEmbeddedSchema()
    {
        var target = Path.Combine(Path.GetTempPath(), "goldpath-manifest.schema.v1.json");
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Goldpath.Cli.goldpath-manifest.schema.json")
            ?? throw new InvalidOperationException("The embedded manifest schema is missing from this build.");
        using var file = File.Create(target);
        stream.CopyTo(file);
        return target;
    }
}
