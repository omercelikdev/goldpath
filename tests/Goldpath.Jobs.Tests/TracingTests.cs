using System.Diagnostics;
using Xunit;

namespace Goldpath.Jobs.Tests;

/// <summary>
/// The runner's span contract (H4): every run is a trace ROOT whose chunks are children,
/// and when the fire was CAUSED by a request (trigger/replay verbs stamp the data map),
/// the run span LINKS to that request's trace — the correlation bridge over the Quartz
/// boundary, where Activity.Current cannot flow.
/// </summary>
public class TracingTests
{
    private static GoldpathJobDefinition Define<TJob>(Action<GoldpathJobBuilder<TJob>>? configure = null)
        where TJob : class, IGoldpathJob
    {
        var options = new GoldpathJobsOptions();
        options.AddJob(configure);
        return options.Jobs[0];
    }

    /// <summary>
    /// Collects stopped Goldpath.Jobs spans for the duration of one test. Listeners are
    /// process-global and xUnit runs classes in parallel, so assertions must filter by
    /// THIS test's run id — never Assert.Single over the raw list.
    /// </summary>
    private sealed class SpanRecorder : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly List<Activity> _spans = [];

        public SpanRecorder()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Goldpath.Jobs",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    lock (_spans)
                    {
                        _spans.Add(activity);
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public IReadOnlyList<Activity> Spans
        {
            get
            {
                lock (_spans)
                {
                    return [.. _spans];
                }
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public async Task A_run_is_a_root_span_with_chunk_children_and_links_the_fire_cause()
    {
        using var recorder = new SpanRecorder();
        using var fixture = new RunnerFixture();
        var job = new ScriptedJob { TotalItems = 4, ChunkSize = 2 };
        var cause = ActivityTraceId.CreateRandom();
        var fire = fixture.Fire() with { TraceParent = $"00-{cause}-{ActivitySpanId.CreateRandom()}-01" };

        var status = await fixture.Runner.RunAsync(job, Define<ScriptedJob>(), fire, CancellationToken.None);
        Assert.Equal(GoldpathJobRunStatus.Completed, status);

        var runId = fixture.Query(db => db.Set<GoldpathJobRun>().Single().Id);
        var run = Assert.Single(recorder.Spans, s => s.OperationName == "goldpath.job.run" && Equals(s.GetTagItem("goldpath.run_id"), runId));
        Assert.Null(run.ParentId);   // a fire has no ambient parent — the run is a trace root
        Assert.Equal(cause, Assert.Single(run.Links).Context.TraceId);
        Assert.Equal("ScriptedJob", run.GetTagItem("goldpath.job"));
        Assert.Equal(GoldpathJobRunStatus.Completed, run.GetTagItem("goldpath.status"));

        var chunks = recorder.Spans.Where(s => s.OperationName == "goldpath.job.chunk" && Equals(s.GetTagItem("goldpath.run_id"), runId)).ToList();
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, chunk =>
        {
            Assert.Equal(run.TraceId, chunk.TraceId);   // ONE trace id covers the whole run
            Assert.Equal(run.SpanId, chunk.ParentSpanId);
        });
        Assert.Equal([0, 1], chunks.Select(c => c.GetTagItem("goldpath.chunk")).Cast<int>().Order().ToArray());
    }

    [Fact]
    public async Task A_garbage_traceparent_is_ignored_never_fatal()
    {
        using var recorder = new SpanRecorder();
        using var fixture = new RunnerFixture();
        var job = new ScriptedJob { TotalItems = 2, ChunkSize = 2 };
        var fire = fixture.Fire() with { TraceParent = "not-a-w3c-traceparent" };

        var status = await fixture.Runner.RunAsync(job, Define<ScriptedJob>(), fire, CancellationToken.None);

        Assert.Equal(GoldpathJobRunStatus.Completed, status);
        var runId = fixture.Query(db => db.Set<GoldpathJobRun>().Single().Id);
        var run = Assert.Single(recorder.Spans, s => s.OperationName == "goldpath.job.run" && Equals(s.GetTagItem("goldpath.run_id"), runId));
        Assert.Empty(run.Links);
    }

    [Fact]
    public async Task A_permanently_failed_run_marks_run_and_chunk_spans_error()
    {
        using var recorder = new SpanRecorder();
        using var fixture = new RunnerFixture();
        var job = new ScriptedJob { TotalItems = 2, ChunkSize = 2, PoisonChunks = { 0 } };

        var status = await fixture.Runner.RunAsync(
            job, Define<ScriptedJob>(b => b.MaxChunkAttempts = 1), fixture.Fire(), CancellationToken.None);

        Assert.Equal(GoldpathJobRunStatus.Failed, status);
        var runId = fixture.Query(db => db.Set<GoldpathJobRun>().Single().Id);
        var run = Assert.Single(recorder.Spans, s => s.OperationName == "goldpath.job.run" && Equals(s.GetTagItem("goldpath.run_id"), runId));
        Assert.Equal(ActivityStatusCode.Error, run.Status);
        var chunk = Assert.Single(recorder.Spans, s => s.OperationName == "goldpath.job.chunk" && Equals(s.GetTagItem("goldpath.run_id"), runId));
        Assert.Equal(ActivityStatusCode.Error, chunk.Status);
        Assert.Contains("poison", chunk.StatusDescription);
    }
}
