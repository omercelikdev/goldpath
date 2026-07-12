using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Goldpath;

/// <summary>Runner seam the Quartz adapter calls — one implementation per app DbContext.</summary>
public interface IGoldpathJobRunner
{
    /// <summary>Runs (or RESUMES) the job's current run; returns the terminal run status.</summary>
    Task<string> RunAsync(IGoldpathJob job, GoldpathJobDefinition definition, GoldpathFireFacts fire, CancellationToken cancellationToken);

    /// <summary>Replays a run's OPEN repair items through the job's replay hook (admin verb).</summary>
    Task<int> ReplayAsync(IGoldpathJob job, Guid runId, GoldpathFireFacts fire, CancellationToken cancellationToken);
}

/// <summary>What the runner needs to know about the Quartz fire hosting it.</summary>
public sealed record GoldpathFireFacts(string SchedulerName, string InstanceName, string FireInstanceId, bool Recovering);

/// <summary>
/// The run engine (jobs RFC §2): plans once, executes chunk by chunk with a persisted
/// checkpoint after every chunk, isolates failures, and RESUMES interrupted runs from the
/// last checkpoint — recovery re-fires land here and continue, never restart. State writes
/// are batched per chunk, never per item (MDM constraint #4).
/// </summary>
public sealed class GoldpathJobRunner<TContext> : IGoldpathJobRunner
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _time;
    private readonly ILogger<GoldpathJobRunner<TContext>> _logger;

    /// <summary>Creates the runner (registered by <c>AddGoldpathJobs</c>).</summary>
    public GoldpathJobRunner(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<GoldpathJobRunner<TContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> RunAsync(IGoldpathJob job, GoldpathJobDefinition definition, GoldpathFireFacts fire, CancellationToken cancellationToken)
    {
        var run = await OpenOrResumeRunAsync(job, definition, fire, cancellationToken);
        GoldpathJobsMetrics.RunStarted(run, _time);

        var claimers = Enumerable.Range(0, Math.Max(1, definition.MaxParallelChunks))
            .Select(_ => ClaimLoopAsync(job, definition, run, fire, cancellationToken))
            .ToList();
        await Task.WhenAll(claimers);

        // Finalize never rides the fire's token: on shutdown it must still record honestly
        // (the run either stays open for resume or closes with what the chunks say).
        var status = await FinalizeAsync(run.Id, CancellationToken.None);
        if (status != GoldpathJobRunStatus.Running)
        {
            GoldpathJobsMetrics.RunFinished(run.Id, status, _time.GetUtcNow() - run.StartedAt);
        }

        return status;
    }

    /// <inheritdoc />
    public async Task<int> ReplayAsync(IGoldpathJob job, Guid runId, GoldpathFireFacts fire, CancellationToken cancellationToken)
    {
        if (job is not IGoldpathItemReplay replayable)
        {
            throw new InvalidOperationException(
                $"{job.GetType().Name} has open repair items but does not implement IGoldpathItemReplay — add the hook to make replay-items work for this job.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var run = await db.Set<GoldpathJobRun>().AsNoTracking().FirstAsync(r => r.Id == runId, cancellationToken)
            ;
        var failures = await db.Set<GoldpathJobItemFailure>()
            .Where(f => f.RunId == runId && f.RedrivenAt == null)
            .OrderBy(f => f.Id)
            .ToListAsync(cancellationToken);

        var context = new GoldpathJobContext(runId, run.SchedulerName, fire.InstanceName, run.JobName, resumed: false, run.InputVersion, scope.ServiceProvider);
        var replayed = 0;
        foreach (var failure in failures)
        {
            await replayable.ReplayItemAsync(failure.ItemKey, context, cancellationToken);
            failure.RedrivenAt = _time.GetUtcNow();
            replayed++;
        }

        await db.SaveChangesAsync(cancellationToken);   // one batched write, per the house rule
        _logger.LogInformation("Run {RunId}: {Count} repair items replayed on {Instance}.", runId, replayed, fire.InstanceName);
        return replayed;
    }

    private async Task<GoldpathJobRun> OpenOrResumeRunAsync(IGoldpathJob job, GoldpathJobDefinition definition, GoldpathFireFacts fire, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var open = await db.Set<GoldpathJobRun>()
            .FirstOrDefaultAsync(r => r.SchedulerName == fire.SchedulerName
                && r.JobName == definition.Name
                && r.Status == GoldpathJobRunStatus.Running, ct);

        if (open is not null)
        {
            // Resume: claims from OTHER fires are stale by definition — the store's
            // DisallowConcurrentExecution guarantees no sibling fire is alive.
            var reclaimed = await db.Set<GoldpathJobRunChunk>()
                .Where(c => c.RunId == open.Id
                    && c.Status == GoldpathJobChunkStatus.Claimed
                    && c.FireInstanceId != fire.FireInstanceId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, GoldpathJobChunkStatus.Pending)
                    .SetProperty(c => c.ClaimedBy, (string?)null)
                    .SetProperty(c => c.FireInstanceId, (string?)null), ct);

            await db.Set<GoldpathJobRun>().Where(r => r.Id == open.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Executions, r => r.Executions + 1), ct);
            open.Executions++;   // keep the in-memory snapshot honest (Resumed flag reads it)

            _logger.LogInformation(
                "Job {Job} run {RunId} RESUMED on {Instance} ({Reclaimed} stale claims reset, {Done}/{Total} chunks already checkpointed).",
                definition.Name, open.Id, fire.InstanceName, reclaimed, open.CompletedChunks, open.TotalChunks);
            return open;
        }

        var now = _time.GetUtcNow();
        var run = new GoldpathJobRun
        {
            Id = Guid.NewGuid(),
            SchedulerName = fire.SchedulerName,
            JobName = definition.Name,
            Status = GoldpathJobRunStatus.Running,
            StartedAt = now,
            DeadlineAt = definition.Deadline is { } deadline ? now + deadline : null,
            StartedBy = fire.InstanceName,
            InputVersion = definition.InputVersionFactory?.Invoke(scope.ServiceProvider),
            Executions = 1,
        };

        var context = new GoldpathJobContext(run.Id, fire.SchedulerName, fire.InstanceName, definition.Name, resumed: false, run.InputVersion, scope.ServiceProvider);
        var plan = await job.PlanAsync(context, ct);

        run.TotalChunks = plan.ChunkPayloads.Count;
        run.TotalItems = plan.TotalItems;
        db.Add(run);
        for (var index = 0; index < plan.ChunkPayloads.Count; index++)
        {
            db.Add(new GoldpathJobRunChunk { RunId = run.Id, Index = index, Payload = plan.ChunkPayloads[index] });
        }

        await db.SaveChangesAsync(ct);   // ONE batched write: the run and its whole plan
        _logger.LogInformation(
            "Job {Job} run {RunId} planned on {Instance}: {Chunks} chunks{Items}.",
            definition.Name, run.Id, fire.InstanceName, run.TotalChunks,
            run.TotalItems is { } items ? $", {items} items" : "");
        return run;
    }

    private async Task ClaimLoopAsync(IGoldpathJob job, GoldpathJobDefinition definition, GoldpathJobRun run, GoldpathFireFacts fire, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();

            var chunk = await db.Set<GoldpathJobRunChunk>()
                .Where(c => c.RunId == run.Id && c.Status == GoldpathJobChunkStatus.Pending)
                .OrderBy(c => c.Index)
                .FirstOrDefaultAsync(ct);
            if (chunk is null)
            {
                return;   // nothing left to claim — completed/claimed-by-siblings/failed
            }

            chunk.Status = GoldpathJobChunkStatus.Claimed;
            chunk.ClaimedBy = fire.InstanceName;
            chunk.FireInstanceId = fire.FireInstanceId;
            chunk.ClaimedAt = _time.GetUtcNow();
            chunk.Attempts++;
            try
            {
                await db.SaveChangesAsync(ct);   // concurrency token on Status: races lose loudly
            }
            catch (DbUpdateConcurrencyException)
            {
                continue;   // a sibling claimed it first — take the next one
            }

            try
            {
                await ExecuteClaimedChunkAsync(job, definition, run, chunk, scope.ServiceProvider, db, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;   // drain: the claim stays; the next fire resets and resumes it
            }

            if (definition.InterChunkDelay is { } delay)
            {
                await Task.Delay(delay, _time, ct);
            }
        }
    }

    private async Task ExecuteClaimedChunkAsync(
        IGoldpathJob job, GoldpathJobDefinition definition, GoldpathJobRun run, GoldpathJobRunChunk chunk,
        IServiceProvider services, TContext db, CancellationToken ct)
    {
        var authored = new GoldpathJobChunk(chunk.Index, chunk.Payload);
        var context = new GoldpathJobContext(run.Id, run.SchedulerName, chunk.ClaimedBy ?? "", run.JobName, resumed: run.Executions > 1, run.InputVersion, services);
        try
        {
            await job.ExecuteChunkAsync(authored, context, ct);

            // THE checkpoint: chunk completion, the job's own tracked work and the item
            // failures commit in ONE batched write — work without checkpoint (or the
            // reverse) can never be observed.
            chunk.Status = GoldpathJobChunkStatus.Completed;
            chunk.CompletedAt = _time.GetUtcNow();
            foreach (var (itemKey, reason) in authored.ItemFailures)
            {
                db.Add(new GoldpathJobItemFailure
                {
                    RunId = run.Id,
                    ChunkIndex = chunk.Index,
                    ItemKey = itemKey,
                    Reason = reason,
                    FailedAt = chunk.CompletedAt.Value,
                });
            }

            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown/interrupt: leave the claim as-is — the next fire reclaims it.
            throw;
        }
        catch (Exception exception)
        {
            // The failure may have come from the CHECKPOINT SAVE itself — this context can
            // hold poisoned tracked entities, so the retreat writes through a FRESH scope
            // with tracking-free updates (a stuck-Claimed chunk was the bug this fixes).
            var exhausted = chunk.Attempts >= definition.MaxChunkAttempts;
            var status = exhausted ? GoldpathJobChunkStatus.Failed : GoldpathJobChunkStatus.Pending;
            var message = exception.Message.Length > 200 ? exception.Message[..200] : exception.Message;
            using var retreat = _scopeFactory.CreateScope();
            var retreatDb = retreat.ServiceProvider.GetRequiredService<TContext>();
            await retreatDb.Set<GoldpathJobRunChunk>().Where(c => c.Id == chunk.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, status)
                    .SetProperty(c => c.LastError, message), CancellationToken.None);
            if (exhausted)
            {
                await retreatDb.Set<GoldpathJobRun>().Where(r => r.Id == run.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.FailedChunks, r => r.FailedChunks + 1), CancellationToken.None);
                _logger.LogError(exception,
                    "Job {Job} run {RunId} chunk {Chunk} FAILED after {Attempts} attempts — isolated, the run continues.",
                    run.JobName, run.Id, chunk.Index, chunk.Attempts);
            }

            return;
        }

        await db.Set<GoldpathJobRun>().Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.CompletedChunks, r => r.CompletedChunks + 1)
                .SetProperty(r => r.ItemFailures, r => r.ItemFailures + authored.ItemFailures.Count), CancellationToken.None);

        var predicted = await UpdatePredictionAsync(db, run.Id, CancellationToken.None);
        GoldpathJobsMetrics.ChunkCompleted(run, authored.ItemFailures.Count, predicted, _time);
    }

    // Prediction is cheap chunk-rate math persisted on the run — the metric/alert layer
    // (S2) reads it; the point is firing BEFORE the deadline does (finance card).
    private async Task<DateTimeOffset?> UpdatePredictionAsync(TContext db, Guid runId, CancellationToken ct)
    {
        var run = await db.Set<GoldpathJobRun>().AsNoTracking().FirstAsync(r => r.Id == runId, ct);
        var done = run.CompletedChunks + run.FailedChunks;
        if (done == 0 || run.TotalChunks == 0)
        {
            return null;
        }

        var elapsed = _time.GetUtcNow() - run.StartedAt;
        var predicted = run.StartedAt + (elapsed / done) * run.TotalChunks;
        await db.Set<GoldpathJobRun>().Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.PredictedFinishAt, predicted), ct);
        return predicted;
    }

    private async Task<string> FinalizeAsync(Guid runId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var unfinished = await db.Set<GoldpathJobRunChunk>()
            .Where(c => c.RunId == runId
                && c.Status != GoldpathJobChunkStatus.Completed
                && c.Status != GoldpathJobChunkStatus.Failed)
            .AnyAsync(ct);
        if (unfinished)
        {
            return GoldpathJobRunStatus.Running;   // interrupted — stays open, the next fire resumes
        }

        var anyFailed = await db.Set<GoldpathJobRunChunk>()
            .AnyAsync(c => c.RunId == runId && c.Status == GoldpathJobChunkStatus.Failed, ct);
        var status = anyFailed ? GoldpathJobRunStatus.Failed : GoldpathJobRunStatus.Completed;
        var now = _time.GetUtcNow();
        await db.Set<GoldpathJobRun>()
            .Where(r => r.Id == runId && r.Status == GoldpathJobRunStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.FinishedAt, now), ct);
        return status;
    }
}
