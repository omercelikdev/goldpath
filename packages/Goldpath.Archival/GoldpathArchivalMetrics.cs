using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Goldpath;

/// <summary>
/// The archival vocabulary (archival RFC §7). Counters accumulate from the engine and the
/// admin verbs; the due-backlog gauge is refreshed by whoever counts (job plans, the
/// definitions view) — the store is the truth, the gauge is its latest reading.
/// </summary>
internal static class GoldpathArchivalMetrics
{
    private static readonly Meter s_meter = new("Goldpath.Archival");
    private static readonly ConcurrentDictionary<string, int> s_backlog = new(StringComparer.Ordinal);

    private static readonly Counter<long> s_appended =
        s_meter.CreateCounter<long>("goldpath_archival_entries_appended_total");

    private static readonly Counter<long> s_purged =
        s_meter.CreateCounter<long>("goldpath_archival_entries_purged_total");

    private static readonly Counter<long> s_erased =
        s_meter.CreateCounter<long>("goldpath_archival_erasures_total");

    private static readonly Counter<long> s_verifyFailures =
        s_meter.CreateCounter<long>("goldpath_archival_verify_failures_total");

    private static readonly Histogram<double> s_retrieval =
        s_meter.CreateHistogram<double>("goldpath_archival_retrieval_seconds");

    static GoldpathArchivalMetrics()
        => s_meter.CreateObservableGauge("goldpath_archival_due_backlog", ObserveBacklog);

    internal static void Appended(string definition, int count)
        => s_appended.Add(count, Tag(definition));

    internal static void Purged(string definition, int count)
        => s_purged.Add(count, Tag(definition));

    internal static void Erased(string definition)
        => s_erased.Add(1, Tag(definition));

    internal static void VerifyFailures(string definition, int count)
        => s_verifyFailures.Add(count, Tag(definition));

    internal static void RetrievalObserved(string definition, TimeSpan elapsed)
        => s_retrieval.Record(elapsed.TotalSeconds, Tag(definition));

    internal static void SetBacklog(string definition, int due)
        => s_backlog[definition] = due;

    private static KeyValuePair<string, object?> Tag(string definition)
        => new("definition", definition);

    private static IEnumerable<Measurement<int>> ObserveBacklog()
    {
        foreach (var (definition, due) in s_backlog)
        {
            yield return new Measurement<int>(due, Tag(definition));
        }
    }
}
