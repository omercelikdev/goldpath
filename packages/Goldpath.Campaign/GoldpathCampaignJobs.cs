using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Goldpath;

/// <summary>
/// The PACER — a LONG-LIVED leader run (campaign RFC D2, constraint 1): the ~1-minute
/// cron only guarantees a leader EXISTS; once running, this run loops in-memory at
/// <see cref="GoldpathCampaignOptions.LeaderTick"/> for one leadership slice, enumerating
/// ahead, releasing under policy (window/quota/TPS/in-flight), sweeping stale claims,
/// and completing campaigns. Death → the next fire takes over from the durable
/// watermarks (counter warmup = reading the row — the D3 reconcile).
/// </summary>
public sealed class GoldpathCampaignPacerJob<TContext> : IGoldpathJob, IGoldpathItemReplay
    where TContext : DbContext
{
    private readonly GoldpathCampaignEngine<TContext> _engine;
    private readonly GoldpathCampaignOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<GoldpathCampaignPacerJob<TContext>> _logger;

    /// <summary>Resolved per fire.</summary>
    public GoldpathCampaignPacerJob(
        GoldpathCampaignEngine<TContext> engine, GoldpathCampaignOptions options,
        TimeProvider time, ILogger<GoldpathCampaignPacerJob<TContext>> logger)
    {
        _engine = engine;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, CancellationToken cancellationToken)
        => Task.FromResult(new GoldpathJobPlan(["lead"]));   // one chunk = one leadership slice

    /// <inheritdoc />
    public async Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, CancellationToken cancellationToken)
    {
        var sliceEnd = _time.GetUtcNow() + _options.LeadershipSlice;
        // Leader-local state (D3's cache tier at single-leader): fractional TPS budgets
        // and open enumeration streams live HERE; the durable rows stay the truth.
        var tpsBudgets = new Dictionary<Guid, double>();
        var streams = new Dictionary<Guid, LeaderStream>();
        try
        {
            while (_time.GetUtcNow() < sliceEnd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tickStart = _time.GetTimestamp();
                // Each tick gets its OWN scope: the runner tracks its checkpoint entities
                // in the fire's scope, and the engine's tracker hygiene must never detach
                // them (a cleared checkpoint = a run that resumes forever).
                using var tickScope = context.Services.CreateScope();
                var db = tickScope.ServiceProvider.GetRequiredService<TContext>();
                var campaigns = await _engine.LoadWorkableAsync(db, cancellationToken);
                db.ChangeTracker.Clear();
                foreach (var campaign in campaigns)
                {
                    await WorkOneTickAsync(tickScope.ServiceProvider, campaign, tpsBudgets, streams, chunk, cancellationToken);
                }

                var elapsed = _time.GetElapsedTime(tickStart);
                if (elapsed < _options.LeaderTick)
                {
                    await Task.Delay(_options.LeaderTick - elapsed, _time, cancellationToken);
                }
            }
        }
        finally
        {
            foreach (var stream in streams.Values)
            {
                await stream.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// One open target stream + the scope it reads through. The stream MUST NOT share the
    /// tick's DbContext: enumeration keeps a data reader open across ticks, and the item
    /// writes would collide with it on the same connection.
    /// </summary>
    private sealed class LeaderStream(IServiceScope scope, IAsyncEnumerator<object> stream) : IAsyncDisposable
    {
        public IAsyncEnumerator<object> Stream { get; } = stream;

        public async ValueTask DisposeAsync()
        {
            await Stream.DisposeAsync();
            scope.Dispose();
        }
    }

    private async Task WorkOneTickAsync(
        IServiceProvider services, GoldpathCampaign campaign,
        Dictionary<Guid, double> tpsBudgets, Dictionary<Guid, LeaderStream> streams,
        GoldpathJobChunk chunk, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TContext>();

        // 1) Enumerate ahead (streaming, ceilinged) while the source has more.
        if (!campaign.EnumerationComplete && campaign.State is GoldpathCampaignState.Created or GoldpathCampaignState.Enumerating or GoldpathCampaignState.Running)
        {
            if (!streams.TryGetValue(campaign.Id, out var stream))
            {
                var streamScope = services.CreateScope();
                try
                {
                    stream = new LeaderStream(streamScope,
                        await _engine.OpenStreamAtWatermarkAsync(streamScope.ServiceProvider, campaign, cancellationToken));
                }
                catch
                {
                    streamScope.Dispose();
                    throw;
                }

                streams[campaign.Id] = stream;
            }

            await _engine.EnumerateStepAsync(services, campaign, stream.Stream, cancellationToken);
            if (campaign.EnumerationComplete && streams.Remove(campaign.Id, out var done))
            {
                await done.DisposeAsync();
            }
        }

        if (campaign.State != GoldpathCampaignState.Running)
        {
            return;   // paused / ceiling-paused / still enumerating first batch
        }

        // 2) Policy math on the LIVE row values (throttle takes effect within one tick).
        var policy = GoldpathCampaignEngine<TContext>.PolicyOf(campaign);
        var now = _time.GetUtcNow();
        await _engine.RollQuotaDayIfNeededAsync(db, campaign, cancellationToken);
        if (!policy.IsWindowOpen(now))
        {
            GoldpathCampaignMetrics.WindowClosed(campaign.Type);
            return;
        }

        var budget = tpsBudgets.GetValueOrDefault(campaign.Id)
            + policy.Tps * _options.LeaderTick.TotalSeconds;
        budget = Math.Min(budget, policy.Tps);   // never bank more than one second of tokens
        var inFlight = campaign.ReleasedThrough - campaign.SucceededCount - campaign.FailedCount;
        var allowance = (int)Math.Floor(Math.Min(budget, Math.Max(0,
            Math.Min(policy.MaxInFlight - inFlight,
                policy.DailyQuota is { } quota ? quota - campaign.ReleasedToday : long.MaxValue))));

        if (allowance > 0)
        {
            var released = await _engine.ReleaseBatchAsync(services, campaign, allowance, cancellationToken);
            budget -= released;
        }

        tpsBudgets[campaign.Id] = budget;

        // 3) Stale-claim sweep: a consumer died between claim and outcome — repair, never resend.
        var staleBefore = now - _options.StaleClaimAfter;
        var stale = await db.Set<GoldpathCampaignItem>()
            .Where(i => i.CampaignId == campaign.Id && i.State == GoldpathCampaignItemState.Processing && i.ClaimedAt < staleBefore)
            .Select(i => i.Seq)
            .Take(100)
            .ToListAsync(cancellationToken);
        if (stale.Count > 0)
        {
            await db.Set<GoldpathCampaignItem>()
                .Where(i => i.CampaignId == campaign.Id && stale.Contains(i.Seq) && i.State == GoldpathCampaignItemState.Processing)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.State, GoldpathCampaignItemState.Failed)
                    .SetProperty(i => i.CompletedAt, now)
                    .SetProperty(i => i.Error, "interrupted mid-flight — confirm the provider, then replay"), cancellationToken);
            await db.Set<GoldpathCampaign>().Where(c => c.Id == campaign.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.FailedCount, c => c.FailedCount + stale.Count), cancellationToken);
            foreach (var seq in stale)
            {
                chunk.ReportItemFailure(
                    string.Create(CultureInfo.InvariantCulture, $"{campaign.Id:N}#{seq}"),
                    "interrupted mid-flight — confirm the provider, then replay");
            }

            _logger.LogWarning("Campaign {CampaignId}: {Count} stale claims swept to the repair queue.", campaign.Id, stale.Count);
        }

        // 4) Completion math (fresh row — the sink may have flushed since we loaded).
        // On the FAILURE flip (token-guarded, so it fires exactly once) the whole failed
        // set files into the repair queue — the jobs replay-items verb is the heal path.
        // Stale-swept items were already filed by an earlier slice; a second entry under
        // this run is harmless (replay of an already-healed item is a no-op).
        if (await _engine.TryCompleteAsync(db, campaign.Id, cancellationToken) == GoldpathCampaignState.CompletedWithFailures)
        {
            var failedItems = await db.Set<GoldpathCampaignItem>()
                .Where(i => i.CampaignId == campaign.Id && i.State == GoldpathCampaignItemState.Failed)
                .Select(i => new { i.Seq, i.Error })
                .ToListAsync(cancellationToken);
            foreach (var failed in failedItems)
            {
                chunk.ReportItemFailure(
                    string.Create(CultureInfo.InvariantCulture, $"{campaign.Id:N}#{failed.Seq}"),
                    failed.Error ?? "failed");
            }
        }

        db.ChangeTracker.Clear();
        GoldpathCampaignMetrics.Snapshot(campaign, inFlight);
    }

    /// <inheritdoc />
    public Task ReplayItemAsync(string itemKey, GoldpathJobContext context, CancellationToken cancellationToken)
        => _engine.ReplayItemAsync(context.Services, itemKey, cancellationToken);
}
