using System.Diagnostics.Metrics;

namespace Goldpath;

/// <summary>
/// The bulk meter (`Goldpath.Bulk`, bulk RFC §7). Execution progress/ETA rides the JOBS meter
/// (a batch executes as a run); this meter owns the intake story: validation volume and
/// quality, dedup hits, and — in S2 — the batches-by-state and awaiting-approval gauges.
/// </summary>
public static class GoldpathBulkMetrics
{
    private static readonly Meter Meter = new("Goldpath.Bulk");

    private static readonly Counter<long> RowsValidated = Meter.CreateCounter<long>(
        "goldpath_bulk_rows_validated_total", description: "Rows that passed validation, per definition.");

    private static readonly Counter<long> RowsInvalid = Meter.CreateCounter<long>(
        "goldpath_bulk_rows_invalid_total", description: "Rows with validation findings, per definition — the ratio is the data-quality signal.");

    private static readonly Counter<long> RowsExecuted = Meter.CreateCounter<long>(
        "goldpath_bulk_rows_executed_total", description: "Rows executed successfully, per definition.");

    private static readonly Counter<long> RowsFailed = Meter.CreateCounter<long>(
        "goldpath_bulk_rows_failed_total", description: "Rows failed into the repair queue, per definition.");

    private static readonly Counter<long> DedupHits = Meter.CreateCounter<long>(
        "goldpath_bulk_dedup_hits_total", description: "Uploads answered by an existing batch (identical bytes) — a spike is a client retry storm.");

    private static readonly Histogram<double> ValidateDuration = Meter.CreateHistogram<double>(
        "goldpath_bulk_validate_duration_seconds", description: "Wall time of one batch validation.");

    internal static void Validated(string definition, int valid, int invalid, double seconds)
    {
        var tag = new KeyValuePair<string, object?>("definition", definition);
        RowsValidated.Add(valid, tag);
        RowsInvalid.Add(invalid, tag);
        ValidateDuration.Record(seconds, tag);
    }

    internal static void Executed(string definition, int executed, int failed)
    {
        var tag = new KeyValuePair<string, object?>("definition", definition);
        RowsExecuted.Add(executed, tag);
        RowsFailed.Add(failed, tag);
    }

    internal static void DedupHit(string definition)
        => DedupHits.Add(1, new KeyValuePair<string, object?>("definition", definition));

    private static readonly object SnapshotLock = new();
    private static readonly Dictionary<string, (IReadOnlyDictionary<string, int> ByState, double OldestAwaitingSeconds)> Snapshots
        = new(StringComparer.Ordinal);

    static GoldpathBulkMetrics()
    {
        Meter.CreateObservableGauge("goldpath_bulk_batches", ObserveStates,
            description: "Batches per definition and state (snapshot from the definitions view / validate run).");
        Meter.CreateObservableGauge("goldpath_bulk_awaiting_approval_age_seconds", ObserveAwaitingAge,
            description: "Age of the OLDEST batch waiting at the approval gate — the human-in-the-loop alert.");
    }

    /// <summary>Publishes one definition's intake snapshot (called by the definitions view and the validate run).</summary>
    public static void SetIntakeSnapshot(string definition, IReadOnlyDictionary<string, int> batchesByState, double oldestAwaitingSeconds)
    {
        lock (SnapshotLock)
        {
            Snapshots[definition] = (batchesByState, oldestAwaitingSeconds);
        }
    }

    private static IEnumerable<Measurement<long>> ObserveStates()
    {
        List<Measurement<long>> measurements = [];
        lock (SnapshotLock)
        {
            foreach (var (definition, snapshot) in Snapshots)
            {
                foreach (var (state, count) in snapshot.ByState)
                {
                    measurements.Add(new Measurement<long>(count,
                        new KeyValuePair<string, object?>("definition", definition),
                        new KeyValuePair<string, object?>("state", state)));
                }
            }
        }

        return measurements;
    }

    private static IEnumerable<Measurement<double>> ObserveAwaitingAge()
    {
        List<Measurement<double>> measurements = [];
        lock (SnapshotLock)
        {
            foreach (var (definition, snapshot) in Snapshots)
            {
                measurements.Add(new Measurement<double>(snapshot.OldestAwaitingSeconds,
                    new KeyValuePair<string, object?>("definition", definition)));
            }
        }

        return measurements;
    }
}
