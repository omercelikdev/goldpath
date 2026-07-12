using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Goldpath.Bulk.Tests;

public class AdminServiceTests : IDisposable
{
    private readonly BulkFixture _fixture = new();
    private readonly GoldpathBulkAdminService<BulkTestContext> _admin;

    public AdminServiceTests()
        => _admin = new GoldpathBulkAdminService<BulkTestContext>(
            _fixture.Engine, _fixture.Options,
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Upload_ingests_and_survives_a_missing_jobs_admin_surface()
    {
        // No GoldpathJobsAdminService registered in the fixture: the immediate trigger is
        // best-effort; the upload must still land (the cron is the safety net).
        var info = await _admin.UploadAsync("payments", BulkFixture.Csv(("E1", "TR1", "10", null)), "a.csv", null, "ops", CancellationToken.None);
        Assert.Equal("Received", info.State);
        Assert.Equal("payments", info.Definition);
        Assert.Equal(1, _fixture.Query(db => db.Set<GoldpathBulkBatch>().Count()));

        var again = await _admin.UploadAsync("payments", BulkFixture.Csv(("E1", "TR1", "10", null)), "a.csv", null, "ops", CancellationToken.None);
        Assert.Equal(info.Id, again.Id);   // dedup speaks through the verb too
    }

    [Fact]
    public async Task Definitions_view_reports_states_and_the_gate_age()
    {
        await _fixture.IngestValidatedAsync(BulkFixture.Csv(("E1", "T", "1", null)));
        await _admin.UploadAsync("payments", BulkFixture.Csv(("E9", "T", "9", null)), "b.csv", null, "ops", CancellationToken.None);

        var status = Assert.Single(await _admin.GetDefinitionsAsync(CancellationToken.None));
        Assert.Equal("payments", status.Name);
        Assert.Equal(1, status.BatchesByState["Validated"]);
        Assert.Equal(1, status.BatchesByState["Received"]);
        Assert.Equal(1, status.AwaitingApproval);
        Assert.NotNull(status.OldestAwaitingApprovalSeconds);
        Assert.True(status.OldestAwaitingApprovalSeconds >= 0);
    }

    [Fact]
    public async Task Batch_queries_filter_by_state_and_fail_closed_on_tenant()
    {
        var mine = await _fixture.IngestValidatedAsync(BulkFixture.Csv(("E1", "T", "1", null)), tenant: "agency-1");
        await _admin.UploadAsync("payments", BulkFixture.Csv(("E2", "T", "2", null)), "b.csv", "agency-2", "ops", CancellationToken.None);

        Assert.Equal(2, (await _admin.GetBatchesAsync(null, null, 10, CancellationToken.None)).Count);
        Assert.Single(await _admin.GetBatchesAsync("validated", null, 10, CancellationToken.None));
        Assert.Single(await _admin.GetBatchesAsync(null, "agency-1", 10, CancellationToken.None));

        Assert.NotNull(await _admin.GetBatchAsync(mine.Id, "agency-1", CancellationToken.None));
        Assert.Null(await _admin.GetBatchAsync(mine.Id, "agency-2", CancellationToken.None));   // fail-closed
        Assert.Null(await _admin.GetBatchAsync(Guid.NewGuid(), null, CancellationToken.None));
    }

    [Fact]
    public async Task The_error_report_pages_by_row_number()
    {
        var batch = await _fixture.IngestValidatedAsync(BulkFixture.Csv(
            ("E1", "T", "-1", null), ("E2", "T", "-2", null), ("E3", "T", "-3", null)));

        var firstPage = await _admin.GetErrorsAsync(batch.Id, afterRowNumber: 0, take: 2, CancellationToken.None);
        Assert.Equal([1, 2], firstPage.Select(e => e.RowNumber));
        var secondPage = await _admin.GetErrorsAsync(batch.Id, afterRowNumber: firstPage[^1].RowNumber, take: 2, CancellationToken.None);
        Assert.Equal([3], secondPage.Select(e => e.RowNumber));
        Assert.All(firstPage, e => Assert.Equal("amount must be positive", e.Message));
    }

    [Fact]
    public async Task Gate_verbs_delegate_to_the_engine_with_the_actor()
    {
        var batch = await _fixture.IngestValidatedAsync(BulkFixture.Csv(("E1", "T", "1", null)));

        Assert.False((await _admin.RejectAsync(batch.Id, "ops", " ", CancellationToken.None)).Ok);   // reason mandatory
        Assert.True((await _admin.ApproveAsync(batch.Id, "treasurer", "four-eyes done", CancellationToken.None)).Ok);

        var decided = _fixture.Query(db => db.Set<GoldpathBulkBatch>().AsNoTracking().Single(b => b.Id == batch.Id));
        Assert.Equal(GoldpathBulkBatchState.Approved, decided.State);
        Assert.Equal("treasurer", decided.DecidedBy);
    }
}
