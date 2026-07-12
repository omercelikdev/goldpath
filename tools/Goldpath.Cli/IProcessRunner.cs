namespace Goldpath.Cli;

/// <summary>
/// The CLI's only side-channel to the outside world (dotnet, specdrift). Injected so every
/// command is testable without processes — and so the tests lock the exact invocations.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Runs a process, streaming its output, and returns the exit code.</summary>
    int Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory);
}
