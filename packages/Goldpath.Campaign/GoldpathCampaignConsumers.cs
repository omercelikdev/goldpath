using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Goldpath;

/// <summary>
/// The competing consumer (campaign RFC D4): CLAIMS the item row (state-guarded) BEFORE
/// the handler's external call — a broker redelivery or a rebalance replay claims zero
/// rows and drops silently; a double-send is structurally impossible (constraint 2).
/// Outcomes are PUBLISHED, not written — the sink batches durable truth (constraint 4).
/// </summary>
public sealed class GoldpathCampaignItemConsumer<TContext> : IConsumer<GoldpathCampaignItemMessage>
    where TContext : DbContext
{
    private readonly GoldpathCampaignEngine<TContext> _engine;
    private readonly TContext _db;
    private readonly IServiceProvider _services;
    private readonly ILogger<GoldpathCampaignItemConsumer<TContext>> _logger;

    /// <summary>Resolved per message (scoped).</summary>
    public GoldpathCampaignItemConsumer(
        GoldpathCampaignEngine<TContext> engine, TContext db, IServiceProvider services,
        ILogger<GoldpathCampaignItemConsumer<TContext>> logger)
    {
        _engine = engine;
        _db = db;
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task Consume(ConsumeContext<GoldpathCampaignItemMessage> context)
    {
        var message = context.Message;
        var item = await _engine.ClaimAsync(_db, message.CampaignId, message.Seq, context.CancellationToken);
        if (item is null)
        {
            return;   // duplicate delivery — someone owns it; dropping IS the correctness
        }

        var campaign = await _db.Set<GoldpathCampaign>().AsNoTracking()
            .SingleAsync(c => c.Id == message.CampaignId, context.CancellationToken);
        try
        {
            await _engine.ExecuteItemAsync(
                _services, message.Type, message.CampaignId, message.Seq, item.TargetJson,
                campaign.Tenant, replay: false, context.CancellationToken);
            await context.Publish(new GoldpathCampaignOutcomeMessage(message.CampaignId, message.Seq, true, null), context.CancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Campaign item {CampaignId}#{Seq} failed in its handler.", message.CampaignId, message.Seq);
            await context.Publish(new GoldpathCampaignOutcomeMessage(
                message.CampaignId, message.Seq, false, e.Message.Length > 1000 ? e.Message[..1000] : e.Message), context.CancellationToken);
        }
    }
}

/// <summary>
/// The outcome SINK (campaign RFC D5, constraint 4): consumes outcome BATCHES and flushes
/// set-based durable updates — 30M items never mean 30M per-item writes.
/// </summary>
public sealed class GoldpathCampaignOutcomeSink<TContext> : IConsumer<Batch<GoldpathCampaignOutcomeMessage>>
    where TContext : DbContext
{
    private readonly GoldpathCampaignEngine<TContext> _engine;
    private readonly TContext _db;

    /// <summary>Resolved per batch (scoped).</summary>
    public GoldpathCampaignOutcomeSink(GoldpathCampaignEngine<TContext> engine, TContext db)
    {
        _engine = engine;
        _db = db;
    }

    /// <inheritdoc />
    public async Task Consume(ConsumeContext<Batch<GoldpathCampaignOutcomeMessage>> context)
    {
        foreach (var group in context.Message.GroupBy(m => m.Message.CampaignId))
        {
            await _engine.ApplyOutcomesAsync(_db, group.Key, [.. group.Select(m => m.Message)], context.CancellationToken);
        }
    }
}

/// <summary>Batches the sink: size/time-bounded flushes, single-threaded per endpoint.</summary>
public sealed class GoldpathCampaignOutcomeSinkDefinition<TContext> : ConsumerDefinition<GoldpathCampaignOutcomeSink<TContext>>
    where TContext : DbContext
{
    /// <inheritdoc />
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<GoldpathCampaignOutcomeSink<TContext>> consumerConfigurator,
        IRegistrationContext context)
        => consumerConfigurator.Options<BatchOptions>(options => options
            .SetMessageLimit(200)
            .SetTimeLimit(TimeSpan.FromMilliseconds(500))
            .SetConcurrencyLimit(1));
}
