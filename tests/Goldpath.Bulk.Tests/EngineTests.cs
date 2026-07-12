using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Goldpath.Bulk.Tests;

public class EngineTests : IDisposable
{
    private readonly BulkFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static IReadOnlyList<(string ItemKey, string Reason)> Failures(GoldpathJobChunk chunk)
        => (List<(string, string)>)typeof(GoldpathJobChunk)
            .GetProperty("ItemFailures", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(chunk)!;

    [Fact]
    public async Task Identical_bytes_return_the_same_batch_but_a_rejected_file_may_be_resubmitted()
    {
        using var scope = _fixture.Scope();
        var (first, created) = await _fixture.Engine.IngestAsync(
            scope.ServiceProvider, "payments", BulkFixture.Csv(("E1", "TR1", "10", null)), "a.csv", null, CancellationToken.None);
        Assert.True(created);

        var (again, createdAgain) = await _fixture.Engine.IngestAsync(
            scope.ServiceProvider, "payments", BulkFixture.Csv(("E1", "TR1", "10", null)), "b.csv", null, CancellationToken.None);
        Assert.False(createdAgain);
        Assert.Equal(first.Id, again.Id);   // the retry storm answers with the SAME batch

        // A rejection is a human decision; resubmitting the same bytes is another one.
        await _fixture.Engine.ValidateBatchAsync(scope.ServiceProvider, first.Id, CancellationToken.None);
        Assert.True((await _fixture.Engine.RejectAsync(scope.ServiceProvider, first.Id, "ops", "wrong cutoff date", CancellationToken.None)).Ok);
        var (resubmitted, createdThird) = await _fixture.Engine.IngestAsync(
            scope.ServiceProvider, "payments", BulkFixture.Csv(("E1", "TR1", "10", null)), "a.csv", null, CancellationToken.None);
        Assert.True(createdThird);
        Assert.NotEqual(first.Id, resubmitted.Id);
    }

    [Fact]
    public async Task Dedup_is_tenant_scoped()
    {
        using var scope = _fixture.Scope();
        var (a, _) = await _fixture.Engine.IngestAsync(
            scope.ServiceProvider, "payments", BulkFixture.Csv(("E1", "TR1", "10", null)), "a.csv", "tenant-a", CancellationToken.None);
        var (b, created) = await _fixture.Engine.IngestAsync(
            scope.ServiceProvider, "payments", BulkFixture.Csv(("E1", "TR1", "10", null)), "a.csv", "tenant-b", CancellationToken.None);
        Assert.True(created);
        Assert.NotEqual(a.Id, b.Id);   // same bytes, different tenants: different batches
    }

    [Fact]
    public async Task Validation_writes_the_report_and_the_counts()
    {
        var batch = await _fixture.IngestValidatedAsync(BulkFixture.Csv(
            ("E1", "TR1", "10", null),
            ("E2", "TR2", "-5", null),      // domain finding
            ("E1", "TR3", "7", null)));     // duplicate row key

        Assert.Equal(GoldpathBulkBatchState.Validated, batch.State);
        Assert.Equal(3, batch.TotalRows);
        Assert.Equal(1, batch.ValidRows);
        Assert.Equal(2, batch.InvalidRows);

        var errors = _fixture.Query(db => db.Set<GoldpathBulkRowError>().AsNoTracking().OrderBy(e => e.RowNumber).ToList());
        Assert.Equal(2, errors.Count);
        Assert.Equal("amount must be positive", errors[0].Message);
        Assert.Equal("(row key)", errors[1].Field);
        Assert.Equal("duplicate of row 1 within the file", errors[1].Message);
        Assert.Equal(1, _fixture.Query(db => db.Set<GoldpathBulkRow>().Count()));   // invalid rows store NO payload
    }

    [Fact]
    public async Task Validation_is_wipe_and_rewrite_idempotent()
    {
        var batch = await _fixture.IngestValidatedAsync(BulkFixture.Csv(("E1", "TR1", "10", null), ("E2", "TR2", "-1", null)));
        _fixture.Mutate(db => db.Set<GoldpathBulkBatch>().Single(b => b.Id == batch.Id).State = GoldpathBulkBatchState.Validating);

        using var scope = _fixture.Scope();
        await _fixture.Engine.ValidateBatchAsync(scope.ServiceProvider, batch.Id, CancellationToken.None);

        var again = _fixture.Query(db => db.Set<GoldpathBulkBatch>().AsNoTracking().Single(b => b.Id == batch.Id));
        Assert.Equal(GoldpathBulkBatchState.Validated, again.State);
        Assert.Equal(1, again.ValidRows);
        Assert.Equal(1, _fixture.Query(db => db.Set<GoldpathBulkRowError>().Count()));   // not doubled
    }

    [Fact]
    public async Task The_row_ceiling_refuses_the_batch_whole()
    {
        using var fixture = new BulkFixture(maxRows: 2);
        using var scope = fixture.Scope();
        var (batch, _) = await fixture.Engine.IngestAsync(
            scope.ServiceProvider, "payments",
            BulkFixture.Csv(("E1", "T", "1", null), ("E2", "T", "1", null), ("E3", "T", "1", null)),
            "big.csv", null, CancellationToken.None);
        await fixture.Engine.ValidateBatchAsync(scope.ServiceProvider, batch.Id, CancellationToken.None);

        var refused = fixture.Query(db => db.Set<GoldpathBulkBatch>().AsNoTracking().Single(b => b.Id == batch.Id));
        Assert.Equal(GoldpathBulkBatchState.Rejected, refused.State);
        Assert.Equal("goldpath", refused.DecidedBy);
        Assert.Equal(0, fixture.Query(db => db.Set<GoldpathBulkRow>().Count()));   // nothing executable survives
        var finding = Assert.Single(fixture.Query(db => db.Set<GoldpathBulkRowError>().ToList()));
        Assert.Equal("(file)", finding.Field);
        Assert.Contains("ceiling (2)", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_gate_blocks_invalid_rows_by_default_and_a_rejection_needs_a_reason()
    {
        var batch = await _fixture.IngestValidatedAsync(BulkFixture.Csv(("E1", "TR1", "10", null), ("E2", "TR2", "-1", null)));

        using var scope = _fixture.Scope();
        var approve = await _fixture.Engine.ApproveAsync(scope.ServiceProvider, batch.Id, "treasurer", null, CancellationToken.None);
        Assert.False(approve.Ok);
        Assert.Contains("1 invalid rows block approval", approve.Message, StringComparison.Ordinal);

        var silentReject = await _fixture.Engine.RejectAsync(scope.ServiceProvider, batch.Id, "treasurer", " ", CancellationToken.None);
        Assert.False(silentReject.Ok);
        Assert.Contains("needs a reason", silentReject.Message, StringComparison.Ordinal);

        Assert.True((await _fixture.Engine.RejectAsync(scope.ServiceProvider, batch.Id, "treasurer", "bad rows", CancellationToken.None)).Ok);
        Assert.False((await _fixture.Engine.ApproveAsync(scope.ServiceProvider, batch.Id, "treasurer", null, CancellationToken.None)).Ok);   // terminal
    }

    [Fact]
    public async Task Tolerating_invalid_rows_executes_the_valid_subset_with_the_report_as_evidence()
    {
        using var fixture = new BulkFixture(b => b.TolerateInvalidRows());
        var batch = await fixture.IngestValidatedAsync(BulkFixture.Csv(("E1", "TR1", "10", null), ("E2", "TR2", "-1", null)));

        using var scope = fixture.Scope();
        var approve = await fixture.Engine.ApproveAsync(scope.ServiceProvider, batch.Id, "treasurer", "partial ok", CancellationToken.None);
        Assert.True(approve.Ok);
        Assert.Contains("1 valid rows will execute", approve.Message, StringComparison.Ordinal);

        await fixture.ExecuteAllAsync(Guid.NewGuid());
        var done = fixture.Query(db => db.Set<GoldpathBulkBatch>().AsNoTracking().Single(b => b.Id == batch.Id));
        Assert.Equal(GoldpathBulkBatchState.Completed, done.State);
        Assert.Equal(1, done.ExecutedRows);
        Assert.Single(fixture.Handler.Executed);
    }

    [Fact]
    public async Task Auto_approve_skips_the_gate_visibly()
    {
        using var fixture = new BulkFixture(b => b.AutoApprove());
        var batch = await fixture.IngestValidatedAsync(BulkFixture.Csv(("E1", "TR1", "10", null)));
        Assert.Equal(GoldpathBulkBatchState.Approved, batch.State);
        Assert.Equal("goldpath:auto-approve", batch.DecidedBy);
    }

    [Fact]
    public async Task Execution_completes_the_batch_and_hands_the_tenant_to_the_handler()
    {
        var batch = await _fixture.IngestValidatedAsync(
            BulkFixture.Csv(("E1", "TR1", "10", null), ("E2", "TR2", "20", null)), tenant: "agency-1");
        using (var scope = _fixture.Scope())
        {
            Assert.True((await _fixture.Engine.ApproveAsync(scope.ServiceProvider, batch.Id, "treasurer", null, CancellationToken.None)).Ok);
        }

        var chunks = await _fixture.ExecuteAllAsync(Guid.NewGuid());
        Assert.All(chunks, c => Assert.Empty(Failures(c)));

        var done = _fixture.Query(db => db.Set<GoldpathBulkBatch>().AsNoTracking().Single(b => b.Id == batch.Id));
        Assert.Equal(GoldpathBulkBatchState.Completed, done.State);
        Assert.Equal(2, done.ExecutedRows);
        Assert.Equal(0, done.FailedRows);
        Assert.NotNull(done.CompletedAt);
        Assert.All(_fixture.Handler.Executed, e => Assert.Equal("agency-1", e.Context.Tenant));
        Assert.Equal([1, 2], _fixture.Handler.Executed.Select(e => e.Context.RowNumber));
    }

    [Fact]
    public async Task A_poisoned_row_lands_in_the_repair_queue_and_replay_flips_the_batch_to_completed()
    {
        var batch = await _fixture.IngestValidatedAsync(BulkFixture.Csv(
            ("E1", "TR1", "10", null), ("E2", "TR2", "20", "FAIL"), ("E3", "TR3", "30", null)));
        using (var scope = _fixture.Scope())
        {
            Assert.True((await _fixture.Engine.ApproveAsync(scope.ServiceProvider, batch.Id, "treasurer", null, CancellationToken.None)).Ok);
        }

        var chunks = await _fixture.ExecuteAllAsync(Guid.NewGuid());
        var failure = Assert.Single(chunks.SelectMany(Failures));
        Assert.Contains("scripted failure for row 2", failure.Reason, StringComparison.Ordinal);

        var partial = _fixture.Query(db => db.Set<GoldpathBulkBatch>().AsNoTracking().Single(b => b.Id == batch.Id));
        Assert.Equal(GoldpathBulkBatchState.CompletedWithFailures, partial.State);
        Assert.Equal(2, partial.ExecutedRows);
        Assert.Equal(1, partial.FailedRows);

        // Fix the world (the handler no longer fails E2 because replay deserializes the stored payload).
        _fixture.Mutate(db =>
        {
            var row = db.Set<GoldpathBulkRow>().Single(r => r.RowNumber == 2);
            row.Payload = row.Payload.Replace("FAIL", "ok", StringComparison.Ordinal);
        });
        using (var scope = _fixture.Scope())
        {
            await _fixture.Engine.ReplayRowAsync(scope.ServiceProvider, failure.ItemKey, CancellationToken.None);
        }

        var healed = _fixture.Query(db => db.Set<GoldpathBulkBatch>().AsNoTracking().Single(b => b.Id == batch.Id));
        Assert.Equal(GoldpathBulkBatchState.Completed, healed.State);
        Assert.Equal(3, healed.ExecutedRows);
        Assert.Equal(0, healed.FailedRows);
        Assert.True(_fixture.Handler.Executed.Last().Context.Replay);
    }

    [Fact]
    public async Task A_claimed_but_unstamped_row_is_repaired_not_resent()
    {
        var batch = await _fixture.IngestValidatedAsync(BulkFixture.Csv(("E1", "TR1", "10", null), ("E2", "TR2", "20", null)));
        using (var scope = _fixture.Scope())
        {
            Assert.True((await _fixture.Engine.ApproveAsync(scope.ServiceProvider, batch.Id, "treasurer", null, CancellationToken.None)).Ok);
        }

        // Simulate the kill-9 window: row 1 was claimed by a previous attempt, never stamped.
        _fixture.Mutate(db => db.Set<GoldpathBulkRow>().Single(r => r.RowNumber == 1).ClaimedAt = DateTimeOffset.UtcNow);

        var chunks = await _fixture.ExecuteAllAsync(Guid.NewGuid());
        var failure = Assert.Single(chunks.SelectMany(Failures));
        Assert.Contains("interrupted mid-flight", failure.Reason, StringComparison.Ordinal);

        // The handler NEVER saw row 1 again — no silent double-send; row 2 executed normally.
        Assert.Equal([2], _fixture.Handler.Executed.Select(e => e.Context.RowNumber));
        var partial = _fixture.Query(db => db.Set<GoldpathBulkBatch>().AsNoTracking().Single(b => b.Id == batch.Id));
        Assert.Equal(GoldpathBulkBatchState.CompletedWithFailures, partial.State);
    }

    [Fact]
    public async Task Adoption_takes_approved_batches_and_orphans_of_dead_runs_but_respects_a_live_run()
    {
        var approved = await _fixture.IngestValidatedAsync(BulkFixture.Csv(("E1", "TR1", "10", null)));
        var orphan = await _fixture.IngestValidatedAsync(BulkFixture.Csv(("E9", "TR9", "9", null)));
        var owned = await _fixture.IngestValidatedAsync(BulkFixture.Csv(("E8", "TR8", "8", null)));
        var deadRun = Guid.NewGuid();
        var liveRun = Guid.NewGuid();
        _fixture.Mutate(db =>
        {
            db.Set<GoldpathBulkBatch>().Single(b => b.Id == approved.Id).State = GoldpathBulkBatchState.Approved;
            var o = db.Set<GoldpathBulkBatch>().Single(b => b.Id == orphan.Id);
            o.State = GoldpathBulkBatchState.Executing;
            o.RunId = deadRun;
            var w = db.Set<GoldpathBulkBatch>().Single(b => b.Id == owned.Id);
            w.State = GoldpathBulkBatchState.Executing;
            w.RunId = liveRun;
            db.Set<GoldpathJobRun>().Add(new GoldpathJobRun { Id = deadRun, JobName = "bulk", Status = GoldpathJobRunStatus.Failed, StartedAt = DateTimeOffset.UtcNow });
            db.Set<GoldpathJobRun>().Add(new GoldpathJobRun { Id = liveRun, JobName = "bulk", Status = GoldpathJobRunStatus.Running, StartedAt = DateTimeOffset.UtcNow });
        });

        using var scope = _fixture.Scope();
        var thisRun = Guid.NewGuid();
        var adopted = await _fixture.Engine.AdoptForExecutionAsync(scope.ServiceProvider, thisRun, CancellationToken.None);

        Assert.Equal(2, adopted.Count);
        Assert.Contains(adopted, b => b.Id == approved.Id);
        Assert.Contains(adopted, b => b.Id == orphan.Id);      // its run died for good — takeover
        Assert.DoesNotContain(adopted, b => b.Id == owned.Id); // a live run keeps its batch
        Assert.All(adopted, b => Assert.Equal(thisRun, b.RunId));
    }

    [Fact]
    public async Task Expired_files_purge_after_the_batch_is_terminal()
    {
        using var fixture = new BulkFixture(b => b.DeleteFileAfter(TimeSpan.FromHours(1)));
        var batch = await fixture.IngestValidatedAsync(BulkFixture.Csv(("E1", "TR1", "10", null)));
        using var scope = fixture.Scope();
        Assert.Equal(0, await fixture.Engine.PurgeExpiredFilesAsync(scope.ServiceProvider, CancellationToken.None));   // not terminal yet

        Assert.True((await fixture.Engine.RejectAsync(scope.ServiceProvider, batch.Id, "ops", "test data", CancellationToken.None)).Ok);
        Assert.Equal(0, await fixture.Engine.PurgeExpiredFilesAsync(scope.ServiceProvider, CancellationToken.None));   // terminal but not aged

        fixture.Mutate(db => db.Set<GoldpathBulkBatch>().Single(b => b.Id == batch.Id).DecidedAt = DateTimeOffset.UtcNow.AddHours(-2));
        Assert.Equal(1, await fixture.Engine.PurgeExpiredFilesAsync(scope.ServiceProvider, CancellationToken.None));
        Assert.Equal(0, fixture.Query(db => db.Set<GoldpathBulkFileChunk>().Count()));
        Assert.Equal(0, fixture.Query(db => db.Set<GoldpathBulkFile>().Count()));
    }
}
