using Xunit;

namespace Goldpath.Jobs.Tests;

public class GoldpathJobRunnerTests
{
    private static GoldpathJobDefinition Define<TJob>(Action<GoldpathJobBuilder<TJob>>? configure = null)
        where TJob : class, IGoldpathJob
    {
        var options = new GoldpathJobsOptions();
        options.AddJob(configure);
        return options.Jobs[0];
    }

    [Fact]
    public async Task A_run_plans_chunks_executes_all_and_completes()
    {
        using var fixture = new RunnerFixture();
        var job = new ScriptedJob { TotalItems = 10, ChunkSize = 3 };

        var status = await fixture.Runner.RunAsync(job, Define<ScriptedJob>(), fixture.Fire(), CancellationToken.None);

        Assert.Equal(GoldpathJobRunStatus.Completed, status);
        Assert.Equal([0, 1, 2, 3], job.ExecutionOrder);   // plan order, exactly once each
        var run = fixture.Query(db => db.Set<GoldpathJobRun>().Single());
        Assert.Equal(4, run.TotalChunks);
        Assert.Equal(4, run.CompletedChunks);
        Assert.Equal(10, run.TotalItems);
        Assert.Equal(GoldpathJobRunStatus.Completed, run.Status);
        Assert.NotNull(run.FinishedAt);
        Assert.Equal("node-a", run.StartedBy);
        Assert.Equal(1, run.Executions);
        Assert.All(fixture.Query(db => db.Set<GoldpathJobRunChunk>().ToList()), c =>
        {
            Assert.Equal(GoldpathJobChunkStatus.Completed, c.Status);
            Assert.Equal("node-a", c.ClaimedBy);         // claims are attributable
            Assert.Equal("fire-1", c.FireInstanceId);
            Assert.NotNull(c.ClaimedAt);
            Assert.NotNull(c.CompletedAt);
            Assert.Equal(1, c.Attempts);
        });
        Assert.True(run.PredictedFinishAt >= run.StartedAt, "prediction can never point before the start");
    }

    [Fact]
    public async Task An_interrupted_run_resumes_from_the_checkpoint_and_never_reexecutes()
    {
        using var fixture = new RunnerFixture();
        using var cts = new CancellationTokenSource();
        var job = new ScriptedJob { TotalItems = 12, ChunkSize = 2, CancelAfterChunks = cts, CancelThreshold = 3 };
        var definition = Define<ScriptedJob>();

        var first = await fixture.Runner.RunAsync(job, definition, fixture.Fire("fire-1"), cts.Token);
        Assert.Equal(GoldpathJobRunStatus.Running, first);   // interrupted — the run stays open
        var executedBefore = job.ExecutedChunks.Count;
        Assert.True(executedBefore < 6, "the interrupt must land before the run finishes");

        var second = await fixture.Runner.RunAsync(job, definition, fixture.Fire("fire-2", "node-b"), CancellationToken.None);

        Assert.Equal(GoldpathJobRunStatus.Completed, second);
        // Exactly-once at chunk level: 6 chunks, 6 executions across BOTH fires.
        Assert.Equal(6, job.ExecutedChunks.Count);
        Assert.Equal([0, 1, 2, 3, 4, 5], job.ExecutedChunks.Order());
        var run = fixture.Query(db => db.Set<GoldpathJobRun>().Single());
        Assert.Equal(2, run.Executions);
        Assert.Contains(true, job.ResumedFlags);   // the second fire knew it was resuming
    }

    [Fact]
    public async Task Stale_claims_from_a_dead_fire_are_reset_and_finished_by_the_next_fire()
    {
        using var fixture = new RunnerFixture();
        using var cts = new CancellationTokenSource();
        var job = new ScriptedJob { TotalItems = 8, ChunkSize = 2, CancelAfterChunks = cts, CancelThreshold = 1 };
        var definition = Define<ScriptedJob>();
        await fixture.Runner.RunAsync(job, definition, fixture.Fire("fire-1"), cts.Token);

        // Simulate a kill-9: one chunk left mid-claim by the dead fire.
        fixture.Mutate(db =>
        {
            var pending = db.Set<GoldpathJobRunChunk>().First(c => c.Status == GoldpathJobChunkStatus.Pending);
            pending.Status = GoldpathJobChunkStatus.Claimed;
            pending.ClaimedBy = "node-a";
            pending.FireInstanceId = "fire-1";
        });

        var status = await fixture.Runner.RunAsync(job, definition, fixture.Fire("fire-2", "node-b"), CancellationToken.None);

        Assert.Equal(GoldpathJobRunStatus.Completed, status);
        Assert.Equal(4, job.ExecutedChunks.Distinct().Count());
        Assert.DoesNotContain(fixture.Query(db => db.Set<GoldpathJobRunChunk>().ToList()),
            c => c.Status == GoldpathJobChunkStatus.Claimed);
    }

    [Fact]
    public async Task A_poison_chunk_is_retried_then_isolated_while_the_run_continues()
    {
        using var fixture = new RunnerFixture();
        var job = new ScriptedJob { TotalItems = 8, ChunkSize = 2 };
        job.PoisonChunks.Add(1);
        var definition = Define<ScriptedJob>(j => j.MaxChunkAttempts = 3);

        // Attempt 1 leaves the chunk Pending again; the loop keeps re-claiming until exhausted.
        var status = await fixture.Runner.RunAsync(job, definition, fixture.Fire(), CancellationToken.None);

        Assert.Equal(GoldpathJobRunStatus.Failed, status);
        var chunks = fixture.Query(db => db.Set<GoldpathJobRunChunk>().OrderBy(c => c.Index).ToList());
        Assert.Equal(GoldpathJobChunkStatus.Failed, chunks[1].Status);
        Assert.Equal(3, chunks[1].Attempts);
        Assert.Contains("poison", chunks[1].LastError, StringComparison.Ordinal);
        Assert.Equal(3, chunks.Count(c => c.Status == GoldpathJobChunkStatus.Completed));
        var run = fixture.Query(db => db.Set<GoldpathJobRun>().Single());
        Assert.Equal(1, run.FailedChunks);
        Assert.Equal(3, run.CompletedChunks);
    }

    [Fact]
    public async Task Job_work_commits_atomically_with_the_checkpoint()
    {
        using var fixture = new RunnerFixture();
        var job = new ScriptedJob
        {
            TotalItems = 4,
            ChunkSize = 2,
            WorkRowKeys = chunk => [chunk.Index * 2, chunk.Index * 2 + 1],
        };

        var status = await fixture.Runner.RunAsync(job, Define<ScriptedJob>(), fixture.Fire(), CancellationToken.None);

        Assert.Equal(GoldpathJobRunStatus.Completed, status);
        Assert.Equal(4, fixture.Query(db => db.WorkRows.Count()));
    }

    [Fact]
    public async Task A_poisoned_CHECKPOINT_save_isolates_the_chunk_instead_of_wedging_it()
    {
        // Regression (caught live by the GmWorkerJobs proof): a job writing a row whose key
        // already exists makes the RUNNER'S checkpoint save throw — the chunk must retreat
        // through a fresh scope (poisoned context discarded), retry, then isolate as
        // Failed; it must NEVER stay Claimed with the exception escaping to Quartz.
        using var fixture = new RunnerFixture();
        fixture.Mutate(db => db.WorkRows.Add(new WorkRow { Key = 2, Value = "pre-existing" }));
        var job = new ScriptedJob
        {
            TotalItems = 4,
            ChunkSize = 2,
            WorkRowKeys = chunk => [chunk.Index * 2, chunk.Index * 2 + 1],   // chunk 1 collides on key 2
        };
        var definition = Define<ScriptedJob>(j => j.MaxChunkAttempts = 2);

        var status = await fixture.Runner.RunAsync(job, definition, fixture.Fire(), CancellationToken.None);

        Assert.Equal(GoldpathJobRunStatus.Failed, status);   // isolated, not escaped
        var chunks = fixture.Query(db => db.Set<GoldpathJobRunChunk>().OrderBy(c => c.Index).ToList());
        Assert.Equal(GoldpathJobChunkStatus.Completed, chunks[0].Status);
        Assert.Equal(GoldpathJobChunkStatus.Failed, chunks[1].Status);
        Assert.Equal(2, chunks[1].Attempts);
        Assert.NotNull(chunks[1].LastError);
        Assert.Equal(1, fixture.Query(db => db.Set<GoldpathJobRun>().Single()).FailedChunks);
    }

    [Fact]
    public async Task Item_failures_land_in_the_repair_queue_without_failing_the_chunk()
    {
        using var fixture = new RunnerFixture();
        var job = new ScriptedJob
        {
            TotalItems = 4,
            ChunkSize = 2,
            ItemFailures = chunk => chunk.Index == 0 ? [("item-3", "ledger mismatch")] : [],
        };

        var status = await fixture.Runner.RunAsync(job, Define<ScriptedJob>(), fixture.Fire(), CancellationToken.None);

        Assert.Equal(GoldpathJobRunStatus.Completed, status);
        var failure = fixture.Query(db => db.Set<GoldpathJobItemFailure>().Single());
        Assert.Equal("item-3", failure.ItemKey);
        Assert.Equal("ledger mismatch", failure.Reason);
        Assert.Equal(0, failure.ChunkIndex);
        Assert.Equal(1, fixture.Query(db => db.Set<GoldpathJobRun>().Single()).ItemFailures);
    }

    [Fact]
    public async Task Deadline_and_pinned_input_version_are_captured_on_the_run()
    {
        using var fixture = new RunnerFixture();
        var job = new ScriptedJob { TotalItems = 2, ChunkSize = 1 };
        var definition = Define<ScriptedJob>(j =>
        {
            j.Deadline = TimeSpan.FromHours(2);
            j.PinInput(_ => "tariff-v42");
        });

        await fixture.Runner.RunAsync(job, definition, fixture.Fire(), CancellationToken.None);

        var run = fixture.Query(db => db.Set<GoldpathJobRun>().Single());
        Assert.NotNull(run.DeadlineAt);
        Assert.Equal("tariff-v42", run.InputVersion);
        Assert.Equal("tariff-v42", job.SeenInputVersion);
        Assert.NotNull(run.PredictedFinishAt);   // chunk-rate prediction persisted
    }

    [Fact]
    public async Task A_completed_run_is_terminal_a_new_fire_starts_a_fresh_run()
    {
        using var fixture = new RunnerFixture();
        var job = new ScriptedJob { TotalItems = 2, ChunkSize = 1 };
        var definition = Define<ScriptedJob>();

        await fixture.Runner.RunAsync(job, definition, fixture.Fire("fire-1"), CancellationToken.None);
        await fixture.Runner.RunAsync(job, definition, fixture.Fire("fire-2"), CancellationToken.None);

        Assert.Equal(2, fixture.Query(db => db.Set<GoldpathJobRun>().Count()));
        Assert.Equal(4, job.ExecutedChunks.Count);
    }
}
