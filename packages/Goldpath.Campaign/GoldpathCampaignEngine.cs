using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Goldpath;

/// <summary>
/// The campaign engine (campaign RFC §2): instance creation, streaming enumeration under
/// the ceiling, policy-governed release, outcome application, completion math. The engine
/// owns STATE; leadership/scheduling belongs to the pacer run, delivery to MassTransit.
/// </summary>
public sealed class GoldpathCampaignEngine<TContext>
    where TContext : DbContext
{
    private readonly GoldpathCampaignOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<GoldpathCampaignEngine<TContext>> _logger;

    /// <summary>Creates the engine.</summary>
    public GoldpathCampaignEngine(GoldpathCampaignOptions options, TimeProvider time, ILogger<GoldpathCampaignEngine<TContext>> logger)
    {
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>Creates a campaign INSTANCE over a registered type (operators launch, developers ship types — D1).</summary>
    public async Task<GoldpathCampaign> CreateAsync(
        IServiceProvider services, string typeKey, string name,
        IReadOnlyDictionary<string, string> parameters, GoldpathCampaignPolicy? policy,
        string? tenant, string actor, CancellationToken cancellationToken)
    {
        var type = _options.Type(typeKey);
        var effective = policy ?? type.DefaultPolicy;
        var db = services.GetRequiredService<TContext>();
        var campaign = new GoldpathCampaign
        {
            Id = Guid.NewGuid(),
            Type = type.Key,
            Name = name,
            State = GoldpathCampaignState.Created,
            ParametersJson = JsonSerializer.Serialize(parameters),
            Tps = effective.Tps,
            DailyQuota = effective.DailyQuota,
            MaxInFlight = effective.MaxInFlight,
            WindowStart = effective.WindowStart,
            WindowEnd = effective.WindowEnd,
            TimeZoneId = effective.TimeZoneId,
            ExcludedDays = effective.ExcludedDays.Count == 0 ? null : string.Join(",", effective.ExcludedDays),
            EndDate = effective.EndDate,
            MaxAttempts = effective.MaxAttempts,
            QuotaDay = effective.LocalDay(_time.GetUtcNow()),
            CreatedAt = _time.GetUtcNow(),
            CreatedBy = actor,
            Tenant = tenant,
        };
        db.Set<GoldpathCampaign>().Add(campaign);
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Campaign {CampaignId} ({Type}) created by {Actor}.", campaign.Id, type.Key, actor);
        return campaign;
    }

    /// <summary>The campaigns a leader slice works: Created (to start), Enumerating, Running.</summary>
    public async Task<List<GoldpathCampaign>> LoadWorkableAsync(TContext db, CancellationToken cancellationToken)
        => await db.Set<GoldpathCampaign>()
            .Where(c => c.State == GoldpathCampaignState.Created
                || c.State == GoldpathCampaignState.Enumerating
                || c.State == GoldpathCampaignState.Running)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <summary>Reads the live policy snapshot off the row (every field is runtime-adjustable, D6).</summary>
    public static GoldpathCampaignPolicy PolicyOf(GoldpathCampaign campaign)
        => new(campaign.Tps, campaign.DailyQuota, campaign.MaxInFlight,
            campaign.WindowStart, campaign.WindowEnd, campaign.TimeZoneId)
        {
            ExcludedDays = GoldpathCampaignPolicy.ParseDays(campaign.ExcludedDays),
            EndDate = campaign.EndDate,
            MaxAttempts = campaign.MaxAttempts,
        };

    /// <summary>
    /// Materializes the next enumeration step (streaming, stable order, resumed BY COUNT
    /// after a takeover). A ceiling breach PAUSES the campaign with a teaching verb — at
    /// L4 scale that is an operator decision, not an automatic partial send.
    /// </summary>
    public async Task<int> EnumerateStepAsync(
        IServiceProvider services, GoldpathCampaign campaign, IAsyncEnumerator<object> stream,
        CancellationToken cancellationToken)
    {
        var type = _options.Type(campaign.Type);
        var db = services.GetRequiredService<TContext>();
        db.Attach(campaign);

        if (campaign.State == GoldpathCampaignState.Created)
        {
            campaign.State = GoldpathCampaignState.Enumerating;
            await db.SaveChangesAsync(cancellationToken);
        }

        var added = 0;
        while (added < _options.EnumerationBatchSize)
        {
            if (!await stream.MoveNextAsync())
            {
                campaign.EnumerationComplete = true;
                if (campaign.State == GoldpathCampaignState.Enumerating)
                {
                    campaign.State = GoldpathCampaignState.Running;
                }

                break;
            }

            if (campaign.EnumeratedThrough >= type.MaxTargets)
            {
                campaign.State = GoldpathCampaignState.Paused;
                campaign.LastVerb = $"goldpath: target ceiling ({type.MaxTargets}) exceeded during enumeration — narrow the selector or raise the type's ceiling, then resume or abort";
                _logger.LogWarning("Campaign {CampaignId} paused: target ceiling {MaxTargets} exceeded.", campaign.Id, type.MaxTargets);
                break;
            }

            campaign.EnumeratedThrough++;
            db.Set<GoldpathCampaignItem>().Add(new GoldpathCampaignItem
            {
                CampaignId = campaign.Id,
                Seq = campaign.EnumeratedThrough,
                TargetJson = type.SerializeTarget(stream.Current),
                State = GoldpathCampaignItemState.Pending,
            });
            added++;
        }

        if (campaign.EnumerationComplete && campaign.State == GoldpathCampaignState.Enumerating)
        {
            campaign.State = GoldpathCampaignState.Running;
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return added;
    }

    /// <summary>Opens the type's target stream, skipped to the campaign's watermark (takeover resume).</summary>
    public async Task<IAsyncEnumerator<object>> OpenStreamAtWatermarkAsync(
        IServiceProvider services, GoldpathCampaign campaign, CancellationToken cancellationToken)
    {
        var type = _options.Type(campaign.Type);
        var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(campaign.ParametersJson) ?? [];
        var stream = type.Enumerate(services, parameters).GetAsyncEnumerator(cancellationToken);
        for (long skip = 0; skip < campaign.EnumeratedThrough; skip++)
        {
            if (!await stream.MoveNextAsync())
            {
                break;   // the source shrank under us; enumeration will just complete
            }
        }

        return stream;
    }

    /// <summary>
    /// Releases up to <paramref name="allowance"/> items: PUBLISH first, then the batched
    /// durable mark (a crash between the two re-publishes on takeover — the consumer's
    /// state-guarded claim makes the duplicate a no-op, never a double-send).
    /// </summary>
    public async Task<int> ReleaseBatchAsync(
        IServiceProvider services, GoldpathCampaign campaign, int allowance, CancellationToken cancellationToken)
    {
        var available = campaign.EnumeratedThrough - campaign.ReleasedThrough;
        var count = (int)Math.Min(Math.Min(allowance, _options.ReleaseBatchSize), available);
        if (count <= 0)
        {
            return 0;
        }

        var db = services.GetRequiredService<TContext>();
        var publisher = services.GetRequiredService<IPublishEndpoint>();
        var from = campaign.ReleasedThrough + 1;
        var to = campaign.ReleasedThrough + count;

        for (var seq = from; seq <= to; seq++)
        {
            await publisher.Publish(new GoldpathCampaignItemMessage(campaign.Id, seq, campaign.Type), cancellationToken);
        }

        await db.Set<GoldpathCampaignItem>()
            .Where(i => i.CampaignId == campaign.Id && i.Seq >= from && i.Seq <= to && i.State == GoldpathCampaignItemState.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.State, GoldpathCampaignItemState.Released), cancellationToken);

        db.Attach(campaign);
        campaign.ReleasedThrough = to;
        campaign.ReleasedToday += count;
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        GoldpathCampaignMetrics.Released(campaign.Type, count);
        return count;
    }

    /// <summary>
    /// Re-releases items whose backoff rung has passed (R1.3): re-publish, then flip
    /// AwaitingRetry back to Released — the same publish-then-mark order as ReleaseBatch,
    /// for the same crash-safety reason. Counts against the SAME tick allowance.
    /// </summary>
    public async Task<int> ReleaseRipeRetriesAsync(
        IServiceProvider services, GoldpathCampaign campaign, int allowance, CancellationToken cancellationToken)
    {
        if (allowance <= 0 || campaign.MaxAttempts <= 1)
        {
            return 0;
        }

        var db = services.GetRequiredService<TContext>();
        var now = _time.GetUtcNow();
        // Ripeness filters BEFORE the allowance truncates (review R3): a backlog of
        // not-yet-ripe low-Seq items must never starve a ripe item behind them. The
        // ladder's two rungs translate to two constant cutoffs the store can index.
        var firstRungBefore = now - GoldpathCampaignPolicy.RetryBackoff(1);
        var secondRungBefore = now - GoldpathCampaignPolicy.RetryBackoff(2);
        var due = await db.Set<GoldpathCampaignItem>().AsNoTracking()
            .Where(i => i.CampaignId == campaign.Id && i.State == GoldpathCampaignItemState.AwaitingRetry
                && ((i.Attempts <= 1 && i.CompletedAt <= firstRungBefore)
                    || (i.Attempts > 1 && i.CompletedAt <= secondRungBefore)))
            .OrderBy(i => i.Seq)
            .Select(i => i.Seq)
            .Take(allowance)
            .ToArrayAsync(cancellationToken);
        if (due.Length == 0)
        {
            return 0;
        }

        var publisher = services.GetRequiredService<IPublishEndpoint>();
        foreach (var seq in due)
        {
            await publisher.Publish(new GoldpathCampaignItemMessage(campaign.Id, seq, campaign.Type), cancellationToken);
        }

        await db.Set<GoldpathCampaignItem>()
            .Where(i => i.CampaignId == campaign.Id && due.Contains(i.Seq) && i.State == GoldpathCampaignItemState.AwaitingRetry)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.State, GoldpathCampaignItemState.Released), cancellationToken);
        GoldpathCampaignMetrics.Released(campaign.Type, due.Length);
        return due.Length;
    }

    /// <summary>Rolls the quota day when the policy-timezone midnight passed (durable — survives takeover).</summary>
    public async Task RollQuotaDayIfNeededAsync(TContext db, GoldpathCampaign campaign, CancellationToken cancellationToken)
    {
        var today = PolicyOf(campaign).LocalDay(_time.GetUtcNow());
        if (campaign.QuotaDay != today)
        {
            db.Attach(campaign);
            campaign.QuotaDay = today;
            campaign.ReleasedToday = 0;
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>The CONSUMER's claim: state-guarded, BEFORE any external call (constraint 2).</summary>
    public async Task<GoldpathCampaignItem?> ClaimAsync(TContext db, Guid campaignId, long seq, CancellationToken cancellationToken)
    {
        var claimed = await db.Set<GoldpathCampaignItem>()
            .Where(i => i.CampaignId == campaignId && i.Seq == seq
                && (i.State == GoldpathCampaignItemState.Released || i.State == GoldpathCampaignItemState.Pending))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.State, GoldpathCampaignItemState.Processing)
                .SetProperty(i => i.ClaimedAt, _time.GetUtcNow()), cancellationToken);
        if (claimed == 0)
        {
            return null;   // a duplicate delivery or a rebalance replay — someone owns it
        }

        return await db.Set<GoldpathCampaignItem>().AsNoTracking()
            .SingleAsync(i => i.CampaignId == campaignId && i.Seq == seq, cancellationToken);
    }

    /// <summary>Runs the type's handler for one claimed item.</summary>
    public Task ExecuteItemAsync(
        IServiceProvider services, string typeKey, Guid campaignId, long seq, string targetJson,
        string? tenant, bool replay, CancellationToken cancellationToken)
        => _options.Type(typeKey).ExecuteItem(
            targetJson, new GoldpathCampaignItemContext(campaignId, seq, typeKey, tenant, replay, services), cancellationToken);

    /// <summary>
    /// Applies one BATCH of outcomes (the sink's flush, constraint 4): two set-based item
    /// updates + relative campaign counters — never a write per item.
    /// </summary>
    public async Task ApplyOutcomesAsync(
        TContext db, Guid campaignId, IReadOnlyList<GoldpathCampaignOutcomeMessage> outcomes, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var succeeded = outcomes.Where(o => o.Succeeded).Select(o => o.Seq).ToArray();
        var failed = outcomes.Where(o => !o.Succeeded).ToArray();
        var maxAttempts = failed.Length == 0
            ? 1
            : await db.Set<GoldpathCampaign>().AsNoTracking()
                .Where(c => c.Id == campaignId).Select(c => c.MaxAttempts).SingleAsync(cancellationToken);

        if (succeeded.Length > 0)
        {
            await db.Set<GoldpathCampaignItem>()
                .Where(i => i.CampaignId == campaignId && succeeded.Contains(i.Seq) && i.State == GoldpathCampaignItemState.Processing)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.State, GoldpathCampaignItemState.Succeeded)
                    .SetProperty(i => i.CompletedAt, now), cancellationToken);
        }

        var exhausted = 0;
        foreach (var failure in failed)
        {
            // R1.3: a transient failure with attempts LEFT waits out its rung and the
            // pacer re-releases it; only exhaustion is a durable failure. Attempts is
            // bumped here — the outcome is the moment an attempt provably happened.
            var landed = await db.Set<GoldpathCampaignItem>()
                .Where(i => i.CampaignId == campaignId && i.Seq == failure.Seq
                    && i.State == GoldpathCampaignItemState.Processing && i.Attempts + 1 < maxAttempts)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.State, GoldpathCampaignItemState.AwaitingRetry)
                    .SetProperty(i => i.Attempts, i => i.Attempts + 1)
                    .SetProperty(i => i.CompletedAt, now)   // = last failure instant; the rung measures from here
                    .SetProperty(i => i.Error, failure.Error), cancellationToken);
            if (landed == 0)
            {
                // Failures carry per-item teaching text; they are the RARE path by design.
                // Counted by ROWS TOUCHED: a stray or duplicate outcome whose guard matches
                // nothing must not inflate FailedCount (the R1 test that found this).
                exhausted += await db.Set<GoldpathCampaignItem>()
                    .Where(i => i.CampaignId == campaignId && i.Seq == failure.Seq && i.State == GoldpathCampaignItemState.Processing)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(i => i.State, GoldpathCampaignItemState.Failed)
                        .SetProperty(i => i.Attempts, i => i.Attempts + 1)
                        .SetProperty(i => i.CompletedAt, now)
                        .SetProperty(i => i.Error, failure.Error), cancellationToken);
            }
        }

        await db.Set<GoldpathCampaign>().Where(c => c.Id == campaignId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.SucceededCount, c => c.SucceededCount + succeeded.Length)
                .SetProperty(c => c.FailedCount, c => c.FailedCount + exhausted), cancellationToken);
        GoldpathCampaignMetrics.Outcomes(campaignId, succeeded.Length, exhausted);
    }

    /// <summary>
    /// Flips a fully-terminal campaign to its completion state (state-token guarded).
    /// Returns the terminal state THIS call stamped, null when nothing changed — the
    /// pacer files the failed set into the repair queue exactly once off that signal.
    /// </summary>
    public async Task<GoldpathCampaignState?> TryCompleteAsync(TContext db, Guid campaignId, CancellationToken cancellationToken)
    {
        try
        {
            var campaign = await db.Set<GoldpathCampaign>().FirstAsync(c => c.Id == campaignId, cancellationToken);
            if (campaign.State != GoldpathCampaignState.Running || !campaign.EnumerationComplete)
            {
                return null;
            }

            var terminal = campaign.SucceededCount + campaign.FailedCount;
            if (campaign.ReleasedThrough < campaign.EnumeratedThrough || terminal < campaign.EnumeratedThrough)
            {
                return null;
            }

            campaign.State = campaign.FailedCount == 0 ? GoldpathCampaignState.Completed : GoldpathCampaignState.CompletedWithFailures;
            campaign.CompletedAt = _time.GetUtcNow();
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Campaign {CampaignId} finished as {State}.", campaignId, campaign.State);
                return campaign.State;
            }
            catch (DbUpdateConcurrencyException)
            {
                return null;   // a verb raced us; the pacer re-evaluates next tick
            }
        }
        finally
        {
            db.ChangeTracker.Clear();   // never leak a tracked campaign into the caller's context
        }
    }

    /// <summary>Replays one FAILED item (the jobs replay-items verb): re-claim, re-execute, apply inline.</summary>
    public async Task ReplayItemAsync(IServiceProvider services, string itemKey, CancellationToken cancellationToken)
    {
        var separator = itemKey.IndexOf('#');
        var campaignId = Guid.ParseExact(itemKey[..separator], "N");
        var seq = long.Parse(itemKey[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        var db = services.GetRequiredService<TContext>();
        var campaign = await db.Set<GoldpathCampaign>().AsNoTracking().SingleAsync(c => c.Id == campaignId, cancellationToken);
        var item = await db.Set<GoldpathCampaignItem>().AsNoTracking()
            .SingleOrDefaultAsync(i => i.CampaignId == campaignId && i.Seq == seq, cancellationToken)
            ?? throw new InvalidOperationException($"No campaign item for repair item '{itemKey}'.");
        if (item.State == GoldpathCampaignItemState.Succeeded)
        {
            return;   // idempotent evidence, not a re-send
        }

        await db.Set<GoldpathCampaignItem>()
            .Where(i => i.CampaignId == campaignId && i.Seq == seq)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.State, GoldpathCampaignItemState.Processing), cancellationToken);
        await ExecuteItemAsync(services, campaign.Type, campaignId, seq, item.TargetJson, campaign.Tenant, replay: true, cancellationToken);
        await ApplyOutcomesAsync(db, campaignId, [new GoldpathCampaignOutcomeMessage(campaignId, seq, true, null)], cancellationToken);
        // Heal the double-count: the original failure already counted once.
        await db.Set<GoldpathCampaign>().Where(c => c.Id == campaignId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.FailedCount, c => c.FailedCount - 1), cancellationToken);
    }
}
