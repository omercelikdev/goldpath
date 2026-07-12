using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Goldpath;

/// <summary>
/// The archive run: one chunk = "move the next batch of one definition". Chunks are
/// idempotent by construction (discovery excludes what already archived), so Jobs'
/// checkpoint/resume semantics apply cleanly; the chain stays single-writer because the
/// job runs with MaxParallelChunks = 1 (enforced by the registration helper).
/// </summary>
public sealed class GoldpathArchiveJob<TContext> : IGoldpathJob
    where TContext : DbContext
{
    private readonly GoldpathArchiveEngine<TContext> _engine;
    private readonly GoldpathArchivalOptions _options;

    /// <summary>Resolved per fire.</summary>
    public GoldpathArchiveJob(GoldpathArchiveEngine<TContext> engine, GoldpathArchivalOptions options)
    {
        _engine = engine;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, CancellationToken cancellationToken)
    {
        var db = context.Services.GetRequiredService<TContext>();
        var payloads = new List<string>();
        long totalItems = 0;
        foreach (var definition in _options.Archives)
        {
            var due = await _engine.CountDueAsync(db, definition, cancellationToken);
            GoldpathArchivalMetrics.SetBacklog(definition.Name, due);
            totalItems += due;
            var chunks = (int)Math.Ceiling(due / (double)_options.BatchSize);
            for (var i = 0; i < chunks; i++)
            {
                payloads.Add(string.Create(CultureInfo.InvariantCulture, $"{definition.Name}|{i}"));
            }
        }

        return new GoldpathJobPlan(payloads, totalItems);
    }

    /// <inheritdoc />
    public async Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, CancellationToken cancellationToken)
    {
        var name = chunk.Payload[..chunk.Payload.IndexOf('|')];
        var definition = _options.Archives.First(d => d.Name == name);
        await _engine.ArchiveNextBatchAsync(context.Services, definition, _options.BatchSize, cancellationToken);
    }
}

/// <summary>
/// The retention purge: expired archive-entry prefixes (legal holds exempt) and row
/// retentions (guarded, ordered, batched deletes). One chunk = one batch of one target.
/// </summary>
public sealed class GoldpathRetentionPurgeJob<TContext> : IGoldpathJob
    where TContext : DbContext
{
    private readonly GoldpathArchiveEngine<TContext> _engine;
    private readonly GoldpathArchivalOptions _options;
    private readonly TimeProvider _time;

    /// <summary>Resolved per fire.</summary>
    public GoldpathRetentionPurgeJob(GoldpathArchiveEngine<TContext> engine, GoldpathArchivalOptions options, TimeProvider time)
    {
        _engine = engine;
        _options = options;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, CancellationToken cancellationToken)
    {
        var db = context.Services.GetRequiredService<TContext>();
        var now = _time.GetUtcNow();
        var payloads = new List<string>();
        long totalItems = 0;

        foreach (var retention in _options.RowRetentions)
        {
            var due = await retention.CountDueCore(db, now, cancellationToken);
            totalItems += due;
            var chunks = (int)Math.Ceiling(due / (double)_options.BatchSize);
            for (var i = 0; i < chunks; i++)
            {
                payloads.Add(string.Create(CultureInfo.InvariantCulture, $"rows:{retention.Name}|{i}"));
            }
        }

        foreach (var definition in _options.Archives.Where(a => a.RetainFor is not null))
        {
            var cutoff = now - definition.RetainFor!.Value;
            var expired = await db.Set<GoldpathArchiveEntry>()
                .CountAsync(e => e.Definition == definition.Name && e.ArchivedAt <= cutoff, cancellationToken);
            totalItems += expired;
            var chunks = (int)Math.Ceiling(expired / (double)_options.BatchSize);
            for (var i = 0; i < chunks; i++)
            {
                payloads.Add(string.Create(CultureInfo.InvariantCulture, $"entries:{definition.Name}|{i}"));
            }
        }

        return new GoldpathJobPlan(payloads, totalItems);
    }

    /// <inheritdoc />
    public async Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, CancellationToken cancellationToken)
    {
        var payload = chunk.Payload;
        var name = payload[(payload.IndexOf(':') + 1)..payload.IndexOf('|')];
        if (payload.StartsWith("rows:", StringComparison.Ordinal))
        {
            var retention = _options.RowRetentions.First(r => r.Name == name);
            var db = context.Services.GetRequiredService<TContext>();
            await retention.PurgeBatchCore(db, _time.GetUtcNow(), _options.BatchSize, cancellationToken);
            return;
        }

        var definition = _options.Archives.First(d => d.Name == name);
        await _engine.PurgeExpiredEntriesAsync(context.Services, definition, _options.BatchSize, cancellationToken);
    }
}

/// <summary>
/// The tamper watch: re-verifies the whole chain slice by slice. Every finding lands in
/// the run's REPAIR QUEUE via <see cref="GoldpathJobChunk.ReportItemFailure"/> — the ops console
/// (and the alert on goldpath_jobs_item_failures) surfaces tampering without new plumbing.
/// </summary>
public sealed class GoldpathArchiveVerifyJob<TContext> : IGoldpathJob
    where TContext : DbContext
{
    private const int SliceSize = 500;
    private readonly GoldpathArchiveEngine<TContext> _engine;
    private readonly GoldpathArchivalOptions _options;

    /// <summary>Resolved per fire.</summary>
    public GoldpathArchiveVerifyJob(GoldpathArchiveEngine<TContext> engine, GoldpathArchivalOptions options)
    {
        _engine = engine;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, CancellationToken cancellationToken)
    {
        var db = context.Services.GetRequiredService<TContext>();
        var payloads = new List<string>();
        foreach (var definition in _options.Archives)
        {
            var state = await db.Set<GoldpathArchiveChainState>().AsNoTracking()
                .FirstOrDefaultAsync(s => s.Definition == definition.Name, cancellationToken);
            if (state is null)
            {
                continue;
            }

            for (var from = state.PurgedThroughIndex + 1; from <= state.LastIndex; from += SliceSize)
            {
                var to = Math.Min(from + SliceSize - 1, state.LastIndex);
                payloads.Add(string.Create(CultureInfo.InvariantCulture, $"{definition.Name}|{from}|{to}"));
            }
        }

        return new GoldpathJobPlan(payloads);
    }

    /// <inheritdoc />
    public async Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, CancellationToken cancellationToken)
    {
        var parts = chunk.Payload.Split('|');
        var definition = _options.Archives.First(d => d.Name == parts[0]);
        var findings = await _engine.VerifySliceAsync(
            context.Services, definition,
            long.Parse(parts[1], CultureInfo.InvariantCulture),
            long.Parse(parts[2], CultureInfo.InvariantCulture),
            cancellationToken);
        if (findings.Count > 0)
        {
            GoldpathArchivalMetrics.VerifyFailures(definition.Name, findings.Count);
        }

        foreach (var finding in findings)
        {
            chunk.ReportItemFailure($"{finding.Definition}#{finding.ChainIndex}", finding.Problem);
        }
    }
}
