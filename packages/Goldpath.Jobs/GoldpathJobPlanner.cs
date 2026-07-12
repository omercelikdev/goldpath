using System.Globalization;

namespace Goldpath;

/// <summary>Plan helpers for the common chunk shapes.</summary>
public static class GoldpathJobPlanner
{
    /// <summary>
    /// Splits a known item count into <c>"start:endExclusive"</c> range payloads — the
    /// keyset-friendly default (jobs load their slice per chunk; nothing materializes).
    /// </summary>
    public static GoldpathJobPlan ByRange(long totalItems, int chunkSize)
    {
        if (totalItems < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalItems), "Item count cannot be negative.");
        }

        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
        }

        var payloads = new List<string>();
        for (long start = 0; start < totalItems; start += chunkSize)
        {
            var end = Math.Min(start + chunkSize, totalItems);
            payloads.Add(string.Create(CultureInfo.InvariantCulture, $"{start}:{end}"));
        }

        return new GoldpathJobPlan(payloads, totalItems);
    }

    /// <summary>Parses a <see cref="ByRange"/> payload back into its bounds.</summary>
    public static (long Start, long EndExclusive) ParseRange(string payload)
    {
        var separator = payload.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0
            || !long.TryParse(payload[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var start)
            || !long.TryParse(payload[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var end)
            || end < start)
        {
            throw new FormatException($"'{payload}' is not a start:endExclusive range payload.");
        }

        return (start, end);
    }
}
