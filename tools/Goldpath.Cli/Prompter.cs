namespace Goldpath.Cli;

/// <summary>
/// The wizard's question seam: pure derivation logic asks THROUGH this, tests answer with
/// a script, the console shell below is the only interactive IO (and is excluded from the
/// mutation gate exactly like <see cref="ConsoleProcessRunner"/> — untestable shells stay
/// thin instead of dragging the gate down).
/// </summary>
public interface IPrompter
{
    /// <summary>One choice out of <paramref name="choices"/> (labels are returned verbatim).</summary>
    string Choose(string question, IReadOnlyList<string> choices, string defaultChoice);

    /// <summary>Zero or more choices out of <paramref name="choices"/>.</summary>
    IReadOnlyList<string> ChooseMany(string question, IReadOnlyList<string> choices);

    /// <summary>A yes/no question.</summary>
    bool Confirm(string question, bool defaultAnswer);

    /// <summary>Free text (the solution name).</summary>
    string Input(string question);
}

/// <summary>Interactive console prompter — numbered choices, comma-separated multi-select.</summary>
public sealed class ConsolePrompter : IPrompter
{
    /// <inheritdoc />
    public string Choose(string question, IReadOnlyList<string> choices, string defaultChoice)
    {
        Console.WriteLine();
        Console.WriteLine($"{question} (default: {defaultChoice})");
        for (var i = 0; i < choices.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {choices[i]}");
        }

        Console.Write("> ");
        var answer = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(answer))
        {
            return defaultChoice;
        }

        return int.TryParse(answer, out var index) && index >= 1 && index <= choices.Count
            ? choices[index - 1]
            : choices.FirstOrDefault(c => string.Equals(c, answer, StringComparison.OrdinalIgnoreCase)) ?? defaultChoice;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ChooseMany(string question, IReadOnlyList<string> choices)
    {
        Console.WriteLine();
        Console.WriteLine($"{question} (comma-separated numbers, empty for none)");
        for (var i = 0; i < choices.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {choices[i]}");
        }

        Console.Write("> ");
        var answer = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(answer))
        {
            return [];
        }

        return answer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => int.TryParse(token, out var index) && index >= 1 && index <= choices.Count
                ? choices[index - 1]
                : choices.FirstOrDefault(c => string.Equals(c, token, StringComparison.OrdinalIgnoreCase)))
            .Where(choice => choice is not null)
            .Select(choice => choice!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public bool Confirm(string question, bool defaultAnswer)
    {
        Console.Write($"\n{question} [{(defaultAnswer ? "Y/n" : "y/N")}] ");
        var answer = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(answer)
            ? defaultAnswer
            : answer.StartsWith("y", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public string Input(string question)
    {
        Console.Write($"\n{question}: ");
        return Console.ReadLine()?.Trim() ?? "";
    }
}
