using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Goldpath;

/// <summary>
/// The intake run: one chunk = validate one Received batch whole (wipe-and-rewrite makes
/// resume honest — validation has no side effects outside its own tables). File-retention
/// purges ride the same run as a trailing chunk.
/// </summary>
public sealed class GoldpathBulkValidateJob<TContext> : IGoldpathJob
    where TContext : DbContext
{
    private readonly GoldpathBulkEngine<TContext> _engine;
    private readonly GoldpathBulkOptions _options;
    private readonly TimeProvider _time;

    /// <summary>Resolved per fire.</summary>
    public GoldpathBulkValidateJob(GoldpathBulkEngine<TContext> engine, GoldpathBulkOptions options, TimeProvider time)
    {
        _engine = engine;
        _options = options;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, CancellationToken cancellationToken)
    {
        var db = context.Services.GetRequiredService<TContext>();
        var pending = await db.Set<GoldpathBulkBatch>().AsNoTracking()
            .Where(b => b.State == GoldpathBulkBatchState.Received || b.State == GoldpathBulkBatchState.Validating)
            .OrderBy(b => b.ReceivedAt)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        // This run fires every minute — publish the intake gauges from here so the
        // batches-by-state and awaiting-approval-age panels stay live unattended.
        var now = _time.GetUtcNow();
        foreach (var definition in _options.Batches)
        {
            var counts = await db.Set<GoldpathBulkBatch>().AsNoTracking()
                .Where(b => b.Definition == definition.Name)
                .GroupBy(b => b.State)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var oldestAwaiting = await db.Set<GoldpathBulkBatch>().AsNoTracking()
                .Where(b => b.Definition == definition.Name && b.State == GoldpathBulkBatchState.Validated)
                .OrderBy(b => b.ValidatedAt)
                .Select(b => b.ValidatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            GoldpathBulkMetrics.SetIntakeSnapshot(
                definition.Name,
                counts.ToDictionary(c => c.Key.ToString(), c => c.Count, StringComparer.Ordinal),
                oldestAwaiting is { } t && counts.Any(c => c.Key == GoldpathBulkBatchState.Validated) ? (now - t).TotalSeconds : 0);
        }

        var payloads = pending
            .Select(id => string.Create(CultureInfo.InvariantCulture, $"validate:{id:N}"))
            .ToList();
        payloads.Add("purge:files");   // retention rides every intake run; a no-op costs nothing
        return new GoldpathJobPlan(payloads, pending.Count);
    }

    /// <inheritdoc />
    public async Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, CancellationToken cancellationToken)
    {
        if (chunk.Payload == "purge:files")
        {
            await _engine.PurgeExpiredFilesAsync(context.Services, cancellationToken);
            return;
        }

        var batchId = Guid.ParseExact(chunk.Payload["validate:".Length..], "N");
        await _engine.ValidateBatchAsync(context.Services, batchId, cancellationToken);
    }
}

/// <summary>
/// The execution run: adopts Approved batches (and orphaned Executing ones whose run died
/// for good), then chunks each over its ROW NUMBER space. Row failures land in THE repair
/// queue; the jobs `replay-items` verb routes back through <see cref="IGoldpathItemReplay"/>.
/// </summary>
public sealed class GoldpathBulkExecuteJob<TContext> : IGoldpathJob, IGoldpathItemReplay
    where TContext : DbContext
{
    private readonly GoldpathBulkEngine<TContext> _engine;
    private readonly GoldpathBulkOptions _options;

    /// <summary>Resolved per fire.</summary>
    public GoldpathBulkExecuteJob(GoldpathBulkEngine<TContext> engine, GoldpathBulkOptions options)
    {
        _engine = engine;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, CancellationToken cancellationToken)
    {
        var adopted = await _engine.AdoptForExecutionAsync(context.Services, context.RunId, cancellationToken);
        var payloads = new List<string>();
        long totalItems = 0;
        foreach (var batch in adopted)
        {
            totalItems += batch.ValidRows;
            // Ranges over row-number VALUES (1-based; invalid rows leave gaps): keyset, not offset.
            for (long start = 1; start <= batch.TotalRows; start += _options.ChunkSize)
            {
                var end = Math.Min(start + _options.ChunkSize, batch.TotalRows + 1);
                payloads.Add(string.Create(CultureInfo.InvariantCulture, $"{batch.Id:N}|{start}:{end}"));
            }
        }

        return new GoldpathJobPlan(payloads, totalItems);
    }

    /// <inheritdoc />
    public async Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, CancellationToken cancellationToken)
    {
        var separator = chunk.Payload.IndexOf('|');
        var batchId = Guid.ParseExact(chunk.Payload[..separator], "N");
        var (start, end) = GoldpathJobPlanner.ParseRange(chunk.Payload[(separator + 1)..]);
        await _engine.ExecuteRangeAsync(context.Services, chunk, batchId, start, end, cancellationToken);
    }

    /// <inheritdoc />
    public Task ReplayItemAsync(string itemKey, GoldpathJobContext context, CancellationToken cancellationToken)
        => _engine.ReplayRowAsync(context.Services, itemKey, cancellationToken);
}
