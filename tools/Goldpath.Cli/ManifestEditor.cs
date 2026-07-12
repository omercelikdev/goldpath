namespace Goldpath.Cli;

/// <summary>
/// Textual manifest edits: the features block is appended/extended in place. The engine —
/// not this editor — is the authority on whether the result is valid; every edit is
/// followed by a specdrift round-trip.
/// </summary>
public static class ManifestEditor
{
    /// <summary>True when the manifest already declares the feature key.</summary>
    public static bool IsEnabled(string manifestText, string manifestKey)
        => manifestText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Any(line => line.TrimEnd().StartsWith($"  {manifestKey}:", StringComparison.Ordinal));

    /// <summary>Kind of the manifest (features may only be added to solutions).</summary>
    public static string? ReadKind(string manifestText)
        => manifestText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(line => line.StartsWith("kind:", StringComparison.Ordinal))
            .Select(line => line["kind:".Length..].Trim())
            .FirstOrDefault();

    /// <summary>
    /// Adds pre-indented feature lines under <c>features:</c>, creating the block at the end
    /// of the file when it does not exist yet (mirrors the template's layout).
    /// </summary>
    public static string AddFeatureLines(string manifestText, IReadOnlyList<string> featureLines)
    {
        var lines = manifestText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var blockIndex = lines.FindIndex(line => line.TrimEnd() == "features:");
        if (blockIndex >= 0)
        {
            lines.InsertRange(blockIndex + 1, featureLines);
            return string.Join('\n', lines);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        lines.Add("features:");
        lines.AddRange(featureLines);
        lines.Add(string.Empty);
        return string.Join('\n', lines);
    }
}
