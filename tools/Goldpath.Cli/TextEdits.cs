namespace Goldpath.Cli;

/// <summary>
/// The textual transform primitives behind <c>goldpath add</c>. Anchor-driven and idempotent by
/// design (RFC D4): the templates ship <c>goldpath:features</c> anchor comments, edits land
/// immediately after them, and a line that already exists is never inserted twice.
/// Roslyn-grade rewriting is explicitly out — this is the same documented boundary the
/// drift engine draws.
/// </summary>
public static class TextEdits
{
    /// <summary>
    /// Inserts <paramref name="newLines"/> as ONE block right after the first line containing
    /// <paramref name="anchor"/>. Idempotence is block-level: when the block's first
    /// meaningful line already exists the whole block is skipped — never per line, because
    /// structural lines (<c>{</c>, <c>});</c>) legitimately repeat across a file.
    /// </summary>
    public static string InsertAfterAnchor(string text, string anchor, IReadOnlyList<string> newLines)
    {
        var lines = SplitLines(text);
        var anchorIndex = lines.FindIndex(line => line.Contains(anchor, StringComparison.Ordinal));
        if (anchorIndex < 0)
        {
            throw new CliFailureException(
                $"anchor '{anchor}' not found — was this app generated from a Goldpath template (or the anchor removed)? Re-add the anchor comment and retry.");
        }

        var probe = newLines.FirstOrDefault(line => line.Trim().Length > 0)?.Trim();
        if (probe is not null && lines.Any(line => line.Trim() == probe))
        {
            return string.Join('\n', lines);
        }

        lines.InsertRange(anchorIndex + 1, newLines);
        return string.Join('\n', lines);
    }

    /// <summary>Removes every line containing <paramref name="marker"/> (used to retire fallbacks).</summary>
    public static string RemoveLinesContaining(string text, string marker)
    {
        var lines = SplitLines(text);
        lines.RemoveAll(line => line.Contains(marker, StringComparison.Ordinal));
        return string.Join('\n', lines);
    }

    /// <summary>True when any line contains the marker (wired-already checks).</summary>
    public static bool ContainsLine(string text, string marker)
        => text.Contains(marker, StringComparison.Ordinal);

    private static List<string> SplitLines(string text)
        => [.. text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];
}
