namespace Goldpath.Cli;

/// <summary>
/// <c>goldpath add feature X</c> — the drift profile's row applied to an EXISTING app: manifest
/// line + package reference + registration + model call (+ feature-specific extras exactly
/// as the template would generate them). Ends with a specdrift round-trip; any finding
/// restores every touched file and fails loudly (RFC Slice C).
/// </summary>
public static class AddFeatureCommand
{
    /// <summary>Applies the feature, verifies with the engine, rolls back on findings.</summary>
    public static int Run(string feature, string appRoot, IProcessRunner runner, TextWriter output, TextWriter error)
    {
        var manifestPath = Path.Combine(appRoot, ".goldpath", "manifest.yaml");
        if (!File.Exists(manifestPath))
        {
            throw new CliFailureException($"no manifest at {manifestPath} — goldpath add runs inside a Goldpath-generated app (or pass --path).");
        }

        var manifest = File.ReadAllText(manifestPath);
        if (ManifestEditor.ReadKind(manifest) is var kind && kind != "solution")
        {
            throw new CliFailureException(
                $"this manifest is kind '{kind ?? "<none>"}' — Ring B features live in the owning SOLUTION's manifest; run goldpath add there.");
        }

        var files = AppFiles.Locate(appRoot);

        var facts = AppFacts.Read(files);
        var plan = FeatureRecipes.Build(feature, facts);

        if (ManifestEditor.IsEnabled(manifest, plan.ManifestKey))
        {
            output.WriteLine($"goldpath: '{feature}' is already enabled ({plan.ManifestKey}) — nothing to do.");
            return 0;
        }

        // Snapshot BEFORE touching anything: the engine is the acceptance test, and a red
        // engine means the app must come back byte-identical.
        var touched = new[] { files.ManifestFile, files.ApiProject, files.AppHostProject, files.ProgramFile, files.ModelFile, files.AppHostFile };
        var snapshot = touched.Distinct(StringComparer.Ordinal).ToDictionary(path => path, File.ReadAllText, StringComparer.Ordinal);

        try
        {
            Apply(plan, files, manifest);

            output.WriteLine($"goldpath: '{feature}' wired — running the engine (specdrift validate + drift)");
            var exitCode = SpecdriftGate.Validate(appRoot, runner);
            if (exitCode == 0)
            {
                exitCode = SpecdriftGate.Drift(appRoot, runner);
            }

            if (exitCode != 0)
            {
                Restore(snapshot);
                error.WriteLine($"goldpath: the engine rejected the result — ALL files restored; fix the findings above and retry ('{feature}' was NOT added).");
                return 1;
            }
        }
        catch
        {
            Restore(snapshot);
            throw;
        }

        if (plan.ModelCalls.Count > 0)
        {
            plan.NextSteps.Add($"the model grew: run `goldpath db add Add{AddWorkerCommand.Pascal(feature)}` and commit the migration (production applies the bundle — migrations RFC D5)");
        }

        output.WriteLine($"goldpath: '{feature}' added — engine clean. Your decisions (goldpath never guesses domain opt-ins):");
        foreach (var step in plan.NextSteps)
        {
            output.WriteLine($"  → {step}");
        }

        return 0;
    }

    private static void Apply(RecipePlan plan, AppFiles files, string manifest)
    {
        File.WriteAllText(files.ManifestFile, ManifestEditor.AddFeatureLines(manifest, plan.ManifestLines));

        if (plan.ApiPackages.Count > 0)
        {
            var references = plan.ApiPackages.Select(p => $"    <PackageReference Include=\"{p}\" />").ToList();
            File.WriteAllText(files.ApiProject, TextEdits.InsertAfterAnchor(File.ReadAllText(files.ApiProject), Anchors.Packages, references));
        }

        if (plan.AppHostPackages.Count > 0)
        {
            var references = plan.AppHostPackages.Select(p => $"    <PackageReference Include=\"{p}\" />").ToList();
            File.WriteAllText(files.AppHostProject, TextEdits.InsertAfterAnchor(File.ReadAllText(files.AppHostProject), Anchors.Packages, references));
        }

        var program = File.ReadAllText(files.ProgramFile);
        foreach (var marker in plan.RemoveFromProgram)
        {
            program = TextEdits.RemoveLinesContaining(program, marker);
        }

        if (plan.Registrations.Count > 0)
        {
            program = TextEdits.InsertAfterAnchor(program, Anchors.Registrations, plan.Registrations);
        }

        if (plan.Middleware.Count > 0)
        {
            program = TextEdits.InsertAfterAnchor(program, Anchors.Middleware, plan.Middleware);
        }

        if (plan.Endpoints.Count > 0)
        {
            program = TextEdits.InsertAfterAnchor(program, Anchors.Endpoints, plan.Endpoints);
        }

        if (plan.JobsOptionsLines.Count > 0)
        {
            program = TextEdits.InsertAfterAnchor(program, Anchors.JobsOptions, plan.JobsOptionsLines);
        }

        if (plan.BusLines.Count > 0)
        {
            program = TextEdits.InsertAfterAnchor(program, Anchors.BusConsumers, plan.BusLines);
        }

        File.WriteAllText(files.ProgramFile, program);

        if (plan.ModelCalls.Count > 0)
        {
            File.WriteAllText(files.ModelFile, TextEdits.InsertAfterAnchor(File.ReadAllText(files.ModelFile), Anchors.Model, plan.ModelCalls));
        }

        if (plan.Resources.Count > 0 || plan.References.Count > 0)
        {
            var appHost = File.ReadAllText(files.AppHostFile);
            if (plan.Resources.Count > 0)
            {
                appHost = TextEdits.InsertAfterAnchor(appHost, Anchors.Resources, plan.Resources);
            }

            if (plan.References.Count > 0)
            {
                appHost = TextEdits.InsertAfterAnchor(appHost, Anchors.References, plan.References);
            }

            File.WriteAllText(files.AppHostFile, appHost);
        }
    }

    private static void Restore(Dictionary<string, string> snapshot)
    {
        foreach (var (path, content) in snapshot)
        {
            File.WriteAllText(path, content);
        }
    }
}
