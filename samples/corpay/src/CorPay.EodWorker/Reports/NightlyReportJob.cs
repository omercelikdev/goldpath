using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CorPay.EodWorker.Reports;

/// <summary>
/// The walking-skeleton job: summarizes the last 30 days in 5-day CHUNKS. After every chunk
/// the runner checkpoints — kill the pod mid-run and another node resumes where it stopped
/// (never from the start). Replace the body with the real aggregation.
/// </summary>
public sealed class NightlyReportJob : IGoldpathJob
{
    /// <inheritdoc />
    public Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, CancellationToken cancellationToken)
        => Task.FromResult(GoldpathJobPlanner.ByRange(totalItems: 30, chunkSize: 5));   // count, never materialize (GP1303)

    /// <inheritdoc />
    public async Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, CancellationToken cancellationToken)
    {
        var db = context.Services.GetRequiredService<ReportsDbContext>();
        var (start, endExclusive) = GoldpathJobPlanner.ParseRange(chunk.Payload);
        for (var dayOffset = (int)start; dayOffset < endExclusive; dayOffset++)
        {
            var row = await db.DailyReports.FindAsync([dayOffset], cancellationToken)
                ?? db.DailyReports.Add(new DailyReportRow { DayOffset = dayOffset }).Entity;
            row.GeneratedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);   // one batched write per chunk (house rule)
    }
}
