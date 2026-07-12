using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Xunit;

namespace Goldpath.Jobs.Tests;

/// <summary>
/// The §7 vocabulary is a contract: names asserted through a real MeterListener while a
/// real run executes — a renamed or silenced metric dies here, not on a blind dashboard.
/// </summary>
public class MetricsTests
{
    [Fact]
    public async Task A_run_emits_the_ladder_vocabulary()
    {
        // The meter is GLOBAL and other tests run in parallel — measurements are filtered
        // to this test's unique scheduler tag.
        var scheduler = $"metrics-{Guid.NewGuid():N}";
        var counters = new ConcurrentDictionary<string, long>();
        var histograms = new ConcurrentDictionary<string, double>();
        var gauges = new ConcurrentDictionary<string, double>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Goldpath.Jobs")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        bool Mine(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "scheduler")
                {
                    return Equals(tag.Value, scheduler);
                }
            }

            return false;
        }

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            if (!Mine(tags))
            {
                return;
            }

            if (instrument is ObservableGauge<long>)
            {
                gauges[instrument.Name] = value;   // repair depth reports as a long gauge
            }
            else
            {
                counters.AddOrUpdate(instrument.Name, value, (_, old) => old + value);
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            if (!Mine(tags))
            {
                return;
            }

            if (instrument is Histogram<double>)
            {
                histograms[instrument.Name] = value;
            }
            else
            {
                gauges[instrument.Name] = value;
            }
        });
        listener.Start();

        using var fixture = new RunnerFixture();
        var job = new ScriptedJob
        {
            TotalItems = 8,
            ChunkSize = 2,
            // Observe the gauges MID-RUN (active runs vanish from the registry on finish).
            ItemFailures = chunk =>
            {
                listener.RecordObservableInstruments();
                return chunk.Index == 1 ? [("item-x", "boom")] : [];
            },
        };
        var options = new GoldpathJobsOptions();
        options.AddJob<ScriptedJob>(j => j.Deadline = TimeSpan.FromHours(1));

        var status = await fixture.Runner.RunAsync(job, options.Jobs[0],
            new GoldpathFireFacts(scheduler, "node-a", "fire-1", Recovering: false), CancellationToken.None);
        listener.RecordObservableInstruments();

        Assert.Equal(GoldpathJobRunStatus.Completed, status);
        Assert.Equal(4, counters["goldpath_jobs_chunks_completed_total"]);
        Assert.Equal(1, counters["goldpath_jobs_item_failures_total"]);
        Assert.True(histograms["goldpath_jobs_run_duration_seconds"] >= 0);
        Assert.True(gauges.ContainsKey("goldpath_jobs_run_progress_ratio"), "progress must be observable during the run");
        Assert.InRange(gauges["goldpath_jobs_run_progress_ratio"], 0, 1);
        Assert.True(gauges["goldpath_jobs_checkpoint_age_seconds"] >= 0);
        Assert.True(gauges.ContainsKey("goldpath_jobs_items_per_second"));
        Assert.True(gauges.ContainsKey("goldpath_jobs_repair_queue_depth"));
    }
}
