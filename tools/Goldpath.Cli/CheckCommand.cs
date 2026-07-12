namespace Goldpath.Cli;

/// <summary>
/// <c>goldpath check</c> — the one verb a team runs before pushing: specdrift validate, specdrift
/// drift, db status (migrations RFC D5), then <c>dotnet build</c>. Stops at the first red step (each later step assumes the
/// earlier one).
/// </summary>
public static class CheckCommand
{
    /// <summary>Runs validate → drift → build, returning the first nonzero exit code.</summary>
    public static int Run(string appRoot, IProcessRunner runner, TextWriter output, TextWriter error)
    {
        output.WriteLine("── goldpath check: specdrift validate");
        var exitCode = SpecdriftGate.Validate(appRoot, runner);
        if (exitCode != 0)
        {
            return exitCode;
        }

        output.WriteLine("── goldpath check: specdrift drift");
        exitCode = SpecdriftGate.Drift(appRoot, runner);
        if (exitCode != 0)
        {
            return exitCode;
        }

        output.WriteLine("── goldpath check: db status (a model change without a migration is drift too)");
        exitCode = DbCommand.StatusForCheck(appRoot, runner, output, error);
        if (exitCode != 0)
        {
            return exitCode;
        }

        output.WriteLine("── goldpath check: dotnet build");
        exitCode = runner.Run("dotnet", ["build", "--nologo"], appRoot);
        if (exitCode == 0)
        {
            output.WriteLine("── goldpath check GREEN");
        }

        return exitCode;
    }
}
