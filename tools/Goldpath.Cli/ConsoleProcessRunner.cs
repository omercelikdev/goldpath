using System.Diagnostics;

namespace Goldpath.Cli;

/// <summary>Real process execution (inherited stdout/stderr). Excluded from mutation: pure IO shell.</summary>
public sealed class ConsoleProcessRunner : IProcessRunner
{
    /// <inheritdoc />
    public int Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"'{fileName}' did not start.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Console.Error.WriteLine($"goldpath: could not run '{fileName}': {exception.Message}");
            if (string.Equals(fileName, "specdrift", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("goldpath: install the engine first: dotnet tool install -g specdrift");
            }

            return 1;
        }
    }
}
