using System.Diagnostics.Metrics;

namespace Goldpath;

/// <summary>
/// The notification meter (`Goldpath.Notification`, RFC §7). Send-run progress rides the JOBS
/// meter; this meter owns the message story: sent/failed/suppressed volume, send latency,
/// dedup hits, and (S2) the oldest-requested-age gauge.
/// </summary>
public static class GoldpathNotificationMetrics
{
    private static readonly Meter Meter = new("Goldpath.Notification");

    private static readonly Counter<long> SentTotal = Meter.CreateCounter<long>(
        "goldpath_notification_sent_total", description: "Messages ACCEPTED by their channel, per template and channel.");

    private static readonly Counter<long> FailedTotal = Meter.CreateCounter<long>(
        "goldpath_notification_failed_total", description: "Messages that exhausted their attempts, per template and channel.");

    private static readonly Counter<long> SuppressedTotal = Meter.CreateCounter<long>(
        "goldpath_notification_suppressed_total", description: "Requests the MaySend hook refused — suppression is evidence.");

    private static readonly Counter<long> DedupHits = Meter.CreateCounter<long>(
        "goldpath_notification_dedup_hits_total", description: "Requests answered by an existing row — a spike is a retry storm.");

    private static readonly Histogram<double> SendDuration = Meter.CreateHistogram<double>(
        "goldpath_notification_send_duration_seconds", description: "Wall time of one accepted send (includes in-attempt retries).");

    internal static void Sent(string template, string channel, TimeSpan elapsed)
    {
        var tags = Tags(template, channel);
        SentTotal.Add(1, tags);
        SendDuration.Record(elapsed.TotalSeconds, tags);
    }

    internal static void Failed(string template, string channel)
        => FailedTotal.Add(1, Tags(template, channel));

    internal static void Suppressed(string template, string channel)
        => SuppressedTotal.Add(1, Tags(template, channel));

    internal static void DedupHit(string template)
        => DedupHits.Add(1, new KeyValuePair<string, object?>("template", template));

    private static readonly object SnapshotLock = new();
    private static readonly Dictionary<string, (IReadOnlyDictionary<string, int> ByState, double OldestRequestedSeconds)> Snapshots
        = new(StringComparer.Ordinal);

    static GoldpathNotificationMetrics()
    {
        Meter.CreateObservableGauge("goldpath_notification_queue", ObserveStates,
            description: "Notifications per template and state (snapshot from the templates view / the send run).");
        Meter.CreateObservableGauge("goldpath_notification_oldest_requested_age_seconds", ObserveQueueAge,
            description: "Age of the OLDEST Requested notification — a stuck channel pages BEFORE customers call.");
    }

    /// <summary>Publishes one template's queue snapshot (the templates view and the send run both call this).</summary>
    public static void SetQueueSnapshot(string template, IReadOnlyDictionary<string, int> byState, double oldestRequestedSeconds)
    {
        lock (SnapshotLock)
        {
            Snapshots[template] = (byState, oldestRequestedSeconds);
        }
    }

    private static IEnumerable<Measurement<long>> ObserveStates()
    {
        List<Measurement<long>> measurements = [];
        lock (SnapshotLock)
        {
            foreach (var (template, snapshot) in Snapshots)
            {
                foreach (var (state, count) in snapshot.ByState)
                {
                    measurements.Add(new Measurement<long>(count,
                        new KeyValuePair<string, object?>("template", template),
                        new KeyValuePair<string, object?>("state", state)));
                }
            }
        }

        return measurements;
    }

    private static IEnumerable<Measurement<double>> ObserveQueueAge()
    {
        List<Measurement<double>> measurements = [];
        lock (SnapshotLock)
        {
            foreach (var (template, snapshot) in Snapshots)
            {
                measurements.Add(new Measurement<double>(snapshot.OldestRequestedSeconds,
                    new KeyValuePair<string, object?>("template", template)));
            }
        }

        return measurements;
    }

    private static KeyValuePair<string, object?>[] Tags(string template, string channel) =>
    [
        new("template", template),
        new("channel", channel),
    ];
}
