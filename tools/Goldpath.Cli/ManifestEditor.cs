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
    public static string? ReadKind(string manifestText) => ReadScalar(manifestText, "kind");

    /// <summary>
    /// A top-level scalar (<c>kind: solution</c>, <c>name: CorPay</c>). Deliberately string
    /// surgery, not a YAML parser: the CLI must never disagree with the ENGINE about what a
    /// manifest MEANS — specdrift validates, this only reports what a reader would see.
    /// Quotes are stripped so <c>name: "CorPay"</c> and <c>name: CorPay</c> read alike.
    /// </summary>
    public static string? ReadScalar(string manifestText, string key)
    {
        var prefix = $"{key}:";
        foreach (var line in manifestText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                var value = line[prefix.Length..].Trim().Trim('"', '\'');
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    /// <summary>
    /// Sets one scalar under <c>providers:</c> (<c>  broker: rabbitmq</c>), replacing the
    /// existing line or adding it to the block; a manifest without a providers block gains
    /// one before <c>features:</c> (or at the end). Same string surgery as the rest — the
    /// engine judges the result.
    /// </summary>
    public static string SetProviderScalar(string manifestText, string key, string value)
    {
        var lines = manifestText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var blockIndex = lines.FindIndex(line => line.TrimEnd() == "providers:");
        var newLine = $"  {key}: {value}";
        if (blockIndex >= 0)
        {
            for (var i = blockIndex + 1; i < lines.Count && lines[i].StartsWith("  ", StringComparison.Ordinal); i++)
            {
                if (lines[i].TrimStart().StartsWith($"{key}:", StringComparison.Ordinal))
                {
                    lines[i] = newLine;
                    return string.Join('\n', lines);
                }
            }

            lines.Insert(blockIndex + 1, newLine);
            return string.Join('\n', lines);
        }

        var featuresIndex = lines.FindIndex(line => line.TrimEnd() == "features:");
        var insertAt = featuresIndex >= 0 ? featuresIndex : lines.FindLastIndex(line => line.Length > 0) + 1;
        lines.InsertRange(insertAt, ["providers:", newLine]);
        return string.Join('\n', lines);
    }

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
