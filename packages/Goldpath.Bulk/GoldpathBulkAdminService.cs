using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Goldpath;

/// <summary>One definition's live intake numbers (the console's landing view).</summary>
public sealed record GoldpathBulkDefinitionStatus(
    string Name,
    IReadOnlyDictionary<string, int> BatchesByState,
    int AwaitingApproval,
    double? OldestAwaitingApprovalSeconds);

/// <summary>One batch over the wire: the state machine's public face.</summary>
public sealed record GoldpathBulkBatchInfo(
    Guid Id,
    string Definition,
    string State,
    string? Tenant,
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    int ExecutedRows,
    int FailedRows,
    Guid? RunId,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ValidatedAt,
    DateTimeOffset? DecidedAt,
    string? DecidedBy,
    string? DecisionNote,
    DateTimeOffset? CompletedAt);

/// <summary>
/// The bulk admin verbs (§7.1: the API is the contract — no screen action exists that curl
/// cannot do). The state machine's rows ARE the audit: upload stamps the batch, the gate
/// stamps the actor and the note, execution stamps the run — every verb leaves durable
/// who/when/what without a separate audit table.
/// </summary>
public sealed class GoldpathBulkAdminService<TContext>
    where TContext : DbContext
{
    private readonly GoldpathBulkEngine<TContext> _engine;
    private readonly GoldpathBulkOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _time;

    /// <summary>Registered by <c>AddGoldpathBulk</c>.</summary>
    public GoldpathBulkAdminService(
        GoldpathBulkEngine<TContext> engine, GoldpathBulkOptions options,
        IServiceScopeFactory scopeFactory, TimeProvider time)
    {
        _engine = engine;
        _options = options;
        _scopeFactory = scopeFactory;
        _time = time;
    }

    /// <summary>
    /// Uploads a file into a definition and, when the jobs admin surface is present, fires
    /// the validate run immediately — the cron stays the safety net, the operator never
    /// waits for it.
    /// </summary>
    public async Task<GoldpathBulkBatchInfo> UploadAsync(
        string definition, Stream content, string fileName, string? tenant, string actor, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var (batch, created) = await _engine.IngestAsync(scope.ServiceProvider, definition, content, fileName, tenant, ct);
        if (created)
        {
            var admin = scope.ServiceProvider.GetService<GoldpathJobsAdminService<TContext>>();
            if (admin is not null)
            {
                var options = scope.ServiceProvider.GetRequiredService<GoldpathJobsOptions>();
                var validateJob = options.Jobs.FirstOrDefault(j => j.JobType.Name.StartsWith("GoldpathBulkValidateJob", StringComparison.Ordinal));
                if (validateJob is not null)
                {
                    // Best effort: a refused trigger (fleet still booting) is NOT an upload failure.
                    await admin.TriggerAsync(options.SchedulerName, validateJob.Name, dryRun: false, actor, ct);
                }
            }
        }

        return ToInfo(batch);
    }

    /// <summary>Every definition with its live intake numbers; feeds the state gauges.</summary>
    public async Task<IReadOnlyList<GoldpathBulkDefinitionStatus>> GetDefinitionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var now = _time.GetUtcNow();
        var result = new List<GoldpathBulkDefinitionStatus>();
        foreach (var definition in _options.Batches)
        {
            var counts = await db.Set<GoldpathBulkBatch>().AsNoTracking()
                .Where(b => b.Definition == definition.Name)
                .GroupBy(b => b.State)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var oldestAwaiting = await db.Set<GoldpathBulkBatch>().AsNoTracking()
                .Where(b => b.Definition == definition.Name && b.State == GoldpathBulkBatchState.Validated)
                .OrderBy(b => b.ValidatedAt)
                .Select(b => b.ValidatedAt)
                .FirstOrDefaultAsync(ct);

            var byState = counts.ToDictionary(c => c.Key.ToString(), c => c.Count, StringComparer.Ordinal);
            var awaiting = byState.GetValueOrDefault(nameof(GoldpathBulkBatchState.Validated));
            double? oldestSeconds = oldestAwaiting is { } t && awaiting > 0 ? (now - t).TotalSeconds : null;
            result.Add(new GoldpathBulkDefinitionStatus(definition.Name, byState, awaiting, oldestSeconds));
            GoldpathBulkMetrics.SetIntakeSnapshot(definition.Name, byState, oldestSeconds ?? 0);
        }

        return result;
    }

    /// <summary>Recent batches, newest first (optionally by state; tenant-scoped when supplied).</summary>
    public async Task<IReadOnlyList<GoldpathBulkBatchInfo>> GetBatchesAsync(
        string? state, string? tenant, int take, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var query = db.Set<GoldpathBulkBatch>().AsNoTracking();
        if (state is not null && Enum.TryParse<GoldpathBulkBatchState>(state, ignoreCase: true, out var parsed))
        {
            query = query.Where(b => b.State == parsed);
        }

        if (tenant is not null)
        {
            query = query.Where(b => b.Tenant == tenant);
        }

        var batches = await query.OrderByDescending(b => b.ReceivedAt).Take(take).ToListAsync(ct);
        return [.. batches.Select(ToInfo)];
    }

    /// <summary>One batch's full story (tenant-scoped: a foreign tenant sees nothing — fail closed).</summary>
    public async Task<GoldpathBulkBatchInfo?> GetBatchAsync(Guid batchId, string? tenant, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var batch = await db.Set<GoldpathBulkBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct);
        return batch is null || (tenant is not null && batch.Tenant != tenant) ? null : ToInfo(batch);
    }

    /// <summary>The validation report, pageable by row number (value-free by construction).</summary>
    public async Task<IReadOnlyList<GoldpathBulkRowError>> GetErrorsAsync(
        Guid batchId, int afterRowNumber, int take, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        return await db.Set<GoldpathBulkRowError>().AsNoTracking()
            .Where(e => e.BatchId == batchId && e.RowNumber > afterRowNumber)
            .OrderBy(e => e.RowNumber).ThenBy(e => e.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    /// <summary>The gate's YES (engine-guarded; the batch row records who/when/why).</summary>
    public async Task<GoldpathAdminResult> ApproveAsync(Guid batchId, string actor, string? note, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        return await _engine.ApproveAsync(scope.ServiceProvider, batchId, actor, note, ct);
    }

    /// <summary>The gate's NO (the note is mandatory — it is the evidence).</summary>
    public async Task<GoldpathAdminResult> RejectAsync(Guid batchId, string actor, string? note, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        return await _engine.RejectAsync(scope.ServiceProvider, batchId, actor, note, ct);
    }

    private static GoldpathBulkBatchInfo ToInfo(GoldpathBulkBatch b) => new(
        b.Id, b.Definition, b.State.ToString(), b.Tenant,
        b.TotalRows, b.ValidRows, b.InvalidRows, b.ExecutedRows, b.FailedRows,
        b.RunId, b.ReceivedAt, b.ValidatedAt, b.DecidedAt, b.DecidedBy, b.DecisionNote, b.CompletedAt);
}
