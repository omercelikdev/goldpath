using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Goldpath;

/// <summary>
/// The ladder-wide metric vocabulary (jobs RFC §7) — ONE set of names every level inherits.
/// Gauges observe the ACTIVE runs of this node; counters/histograms accumulate. Deadline
/// prediction is a metric so the alert fires BEFORE the deadline does, never after.
/// </summary>
internal static class GoldpathJobsMetrics
{
    private static readonly Meter s_meter = new("Goldpath.Jobs");
    private static readonly ConcurrentDictionary<Guid, ActiveRun> s_active = new();

    private static readonly Counter<long> s_itemFailures =
        s_meter.CreateCounter<long>("goldpath_jobs_item_failures_total");

    private static readonly Counter<long> s_chunksCompleted =
        s_meter.CreateCounter<long>("goldpath_jobs_chunks_completed_total");

    private static readonly Histogram<double> s_runDuration =
        s_meter.CreateHistogram<double>("goldpath_jobs_run_duration_seconds");

    static GoldpathJobsMetrics()
    {
        s_meter.CreateObservableGauge("goldpath_jobs_run_progress_ratio", ObserveProgress);
        s_meter.CreateObservableGauge("goldpath_jobs_items_per_second", ObserveItemRate);
        s_meter.CreateObservableGauge("goldpath_jobs_checkpoint_age_seconds", ObserveCheckpointAge);
        s_meter.CreateObservableGauge("goldpath_jobs_predicted_finish_seconds", ObservePredictedFinish);
        s_meter.CreateObservableGauge("goldpath_jobs_repair_queue_depth", ObserveRepairDepth);
    }

    private sealed class ActiveRun
    {
        public required string Scheduler;
        public required string Job;
        public required DateTimeOffset StartedAt;
        public required int TotalChunks;
        public long? TotalItems;
        public DateTimeOffset? DeadlineAt;
        public int CompletedChunks;
        public int ItemFailures;
        public DateTimeOffset LastCheckpoint;
        public DateTimeOffset? PredictedFinishAt;

        public KeyValuePair<string, object?>[] Tags =>
            [new("scheduler", Scheduler), new("job", Job)];
    }

    internal static void RunStarted(GoldpathJobRun run, TimeProvider time)
        => s_active[run.Id] = new ActiveRun
        {
            Scheduler = run.SchedulerName,
            Job = run.JobName,
            StartedAt = run.StartedAt,
            TotalChunks = run.TotalChunks,
            TotalItems = run.TotalItems,
            DeadlineAt = run.DeadlineAt,
            CompletedChunks = run.CompletedChunks,
            ItemFailures = run.ItemFailures,
            LastCheckpoint = time.GetUtcNow(),
        };

    internal static void ChunkCompleted(GoldpathJobRun run, int newItemFailures, DateTimeOffset? predictedFinish, TimeProvider time)
    {
        if (!s_active.TryGetValue(run.Id, out var active))
        {
            return;
        }

        Interlocked.Increment(ref active.CompletedChunks);
        Interlocked.Add(ref active.ItemFailures, newItemFailures);
        active.LastCheckpoint = time.GetUtcNow();
        active.PredictedFinishAt = predictedFinish;

        s_chunksCompleted.Add(1, active.Tags);
        if (newItemFailures > 0)
        {
            s_itemFailures.Add(newItemFailures, active.Tags);
        }
    }

    internal static void RunFinished(Guid runId, string status, TimeSpan duration)
    {
        if (s_active.TryRemove(runId, out var active))
        {
            s_runDuration.Record(duration.TotalSeconds,
                [.. active.Tags, new KeyValuePair<string, object?>("status", status)]);
        }
    }

    private static IEnumerable<Measurement<double>> ObserveProgress()
    {
        foreach (var run in s_active.Values)
        {
            yield return new Measurement<double>(
                run.TotalChunks == 0 ? 0 : (double)run.CompletedChunks / run.TotalChunks, run.Tags);
        }
    }

    private static IEnumerable<Measurement<double>> ObserveItemRate()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var run in s_active.Values)
        {
            var elapsed = (now - run.StartedAt).TotalSeconds;
            if (elapsed <= 0 || run.TotalItems is not { } totalItems || run.TotalChunks == 0)
            {
                continue;
            }

            // Chunks carry opaque payloads; the honest rate uses the plan's average density.
            var itemsDone = (double)totalItems * run.CompletedChunks / run.TotalChunks;
            yield return new Measurement<double>(itemsDone / elapsed, run.Tags);
        }
    }

    private static IEnumerable<Measurement<double>> ObserveCheckpointAge()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var run in s_active.Values)
        {
            yield return new Measurement<double>((now - run.LastCheckpoint).TotalSeconds, run.Tags);
        }
    }

    private static IEnumerable<Measurement<double>> ObservePredictedFinish()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var run in s_active.Values)
        {
            if (run.DeadlineAt is not { } deadline || run.PredictedFinishAt is not { } predicted)
            {
                continue;
            }

            // Positive = margin before the deadline; NEGATIVE = predicted overrun (alert).
            yield return new Measurement<double>((deadline - predicted).TotalSeconds, run.Tags);
        }
    }

    private static IEnumerable<Measurement<long>> ObserveRepairDepth()
    {
        foreach (var run in s_active.Values)
        {
            yield return new Measurement<long>(run.ItemFailures, run.Tags);
        }
    }
}
