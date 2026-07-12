using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Goldpath;

/// <summary>
/// The app's execution hook: one claimed target. Throwing marks the item Failed (the
/// repair story applies). The item was CLAIMED (persisted) before this runs — a broker
/// redelivery cannot double-execute it (constraint 2). Do NOT call SaveChanges here:
/// outcomes flow through the batching sink (constraint 4, GP1702).
/// </summary>
public interface IGoldpathCampaignItemHandler<in TTarget>
    where TTarget : class
{
    /// <summary>Executes one target.</summary>
    Task ExecuteAsync(TTarget target, GoldpathCampaignItemContext context, CancellationToken cancellationToken);
}

/// <summary>Ambient facts handed to the item handler.</summary>
public sealed class GoldpathCampaignItemContext
{
    internal GoldpathCampaignItemContext(Guid campaignId, long seq, string type, string? tenant, bool replay, IServiceProvider services)
    {
        CampaignId = campaignId;
        Seq = seq;
        Type = type;
        Tenant = tenant;
        Replay = replay;
        Services = services;
    }

    /// <summary>The owning campaign instance.</summary>
    public Guid CampaignId { get; }

    /// <summary>The item's sequence (the repair coordinate).</summary>
    public long Seq { get; }

    /// <summary>The campaign-type key.</summary>
    public string Type { get; }

    /// <summary>The campaign's tenant, when tenant-bound.</summary>
    public string? Tenant { get; }

    /// <summary>True when this invocation is an admin replay of a failed item.</summary>
    public bool Replay { get; }

    /// <summary>Scoped services of the executing consumer.</summary>
    public IServiceProvider Services { get; }
}

/// <summary>The live policy snapshot the pacer evaluates each tick (all fields runtime-adjustable, D6).</summary>
public sealed record GoldpathCampaignPolicy(
    int Tps,
    int? DailyQuota,
    int MaxInFlight,
    TimeOnly? WindowStart,
    TimeOnly? WindowEnd,
    string TimeZoneId)
{
    /// <summary>True when <paramref name="utcNow"/> falls inside the send window (always true when no window).</summary>
    public bool IsWindowOpen(DateTimeOffset utcNow)
    {
        if (WindowStart is null || WindowEnd is null)
        {
            return true;
        }

        var local = TimeOnly.FromTimeSpan(TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)).TimeOfDay);
        return WindowStart <= WindowEnd
            ? local >= WindowStart && local < WindowEnd
            : local >= WindowStart || local < WindowEnd;   // overnight window (22:00–06:00)
    }

    /// <summary>The policy-timezone calendar day (quota accounting).</summary>
    public DateOnly LocalDay(DateTimeOffset utcNow)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)).Date);
}

/// <summary>One registered campaign type — code, shipped through PRs (D1). Closures baked.</summary>
public sealed class GoldpathCampaignType
{
    internal GoldpathCampaignType(string key) => Key = key;

    /// <summary>The registration key instances reference.</summary>
    public string Key { get; }

    /// <summary>The enumeration ceiling — MANDATORY (an unbounded L4 enumeration is an outage, GP1701).</summary>
    public long MaxTargets { get; internal set; }

    /// <summary>Default policy for new instances (operators may override per instance).</summary>
    public GoldpathCampaignPolicy DefaultPolicy { get; internal set; }
        = new(Tps: 50, DailyQuota: null, MaxInFlight: 1_000, WindowStart: null, WindowEnd: null, TimeZoneId: "UTC");

    internal Func<IServiceProvider, IReadOnlyDictionary<string, string>, IAsyncEnumerable<object>> Enumerate { get; set; } = null!;

    internal Func<string, GoldpathCampaignItemContext, CancellationToken, Task> ExecuteItem { get; set; } = null!;

    internal Func<object, string> SerializeTarget { get; set; } = null!;
}

/// <summary>Fluent registration surface for one campaign type.</summary>
public sealed class GoldpathCampaignTypeBuilder<TTarget>
    where TTarget : class
{
    private readonly GoldpathCampaignType _type;
    private Func<IServiceProvider, IReadOnlyDictionary<string, string>, IAsyncEnumerable<TTarget>>? _targets;

    internal GoldpathCampaignTypeBuilder(GoldpathCampaignType type) => _type = type;

    /// <summary>Sets the mandatory enumeration ceiling.</summary>
    public GoldpathCampaignTypeBuilder<TTarget> MaxTargets(long maxTargets)
    {
        if (maxTargets <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTargets), "The target ceiling must be positive.");
        }

        _type.MaxTargets = maxTargets;
        return this;
    }

    /// <summary>
    /// Binds the streaming target selector. Resolve YOUR DbContext from the provider and
    /// return a keyset-ORDERED stream — the enumerator checkpoints by count, so the order
    /// must be stable across a leader takeover.
    /// </summary>
    public GoldpathCampaignTypeBuilder<TTarget> Targets(
        Func<IServiceProvider, IReadOnlyDictionary<string, string>, IAsyncEnumerable<TTarget>> targets)
    {
        _targets = targets;
        return this;
    }

    /// <summary>Sets the default policy new instances start from.</summary>
    public GoldpathCampaignTypeBuilder<TTarget> DefaultPolicy(Func<GoldpathCampaignPolicy, GoldpathCampaignPolicy> configure)
    {
        _type.DefaultPolicy = configure(_type.DefaultPolicy);
        return this;
    }

    internal void Bake()
    {
        if (_type.MaxTargets == 0)
        {
            throw new InvalidOperationException(
                $"Campaign type '{_type.Key}' has no MaxTargets — an unbounded enumeration at L4 scale is an outage, not a campaign (campaign RFC D7 / GP1701).");
        }

        var targets = _targets ?? throw new InvalidOperationException(
            $"Campaign type '{_type.Key}' has no Targets selector — a campaign that cannot enumerate is a typo.");

        _type.Enumerate = (services, parameters) => Upcast(targets(services, parameters));
        _type.SerializeTarget = target => JsonSerializer.Serialize((TTarget)target);
        _type.ExecuteItem = async (targetJson, context, cancellationToken) =>
        {
            var target = JsonSerializer.Deserialize<TTarget>(targetJson)
                ?? throw new InvalidOperationException($"Campaign item payload of '{_type.Key}' deserialized to null.");
            var handler = context.Services.GetRequiredService<IGoldpathCampaignItemHandler<TTarget>>();
            await handler.ExecuteAsync(target, context, cancellationToken);
        };
    }

    private static async IAsyncEnumerable<object> Upcast(IAsyncEnumerable<TTarget> source)
    {
        await foreach (var item in source)
        {
            yield return item;
        }
    }
}

/// <summary>
/// Campaign composition options (campaign RFC §4). Types bake their closures at
/// registration; the engine, the pacer and the consumers stay non-generic.
/// </summary>
public sealed class GoldpathCampaignOptions
{
    internal Dictionary<string, GoldpathCampaignType> TypeMap { get; } = new(StringComparer.Ordinal);

    /// <summary>The registered campaign types.</summary>
    public IReadOnlyCollection<GoldpathCampaignType> Types => TypeMap.Values;

    /// <summary>Items materialized per enumeration step (each step is one durable write).</summary>
    public int EnumerationBatchSize { get; set; } = 2_000;

    /// <summary>Items released per broker publish batch (bounded so a tick stays cheap).</summary>
    public int ReleaseBatchSize { get; set; } = 500;

    /// <summary>The leader's in-memory tick (constraint 1: local ticks, not cluster locks).</summary>
    public TimeSpan LeaderTick { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How long one pacer RUN leads before returning (the cron re-fires it).</summary>
    public TimeSpan LeadershipSlice { get; set; } = TimeSpan.FromSeconds(50);

    /// <summary>A Processing claim older than this is an interrupted consumer (sweep to repair).</summary>
    public TimeSpan StaleClaimAfter { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Registers one campaign type.</summary>
    public GoldpathCampaignOptions AddCampaign<TTarget>(string key, Action<GoldpathCampaignTypeBuilder<TTarget>> configure)
        where TTarget : class
    {
        if (TypeMap.ContainsKey(key))
        {
            throw new InvalidOperationException($"Campaign type '{key}' is already registered.");
        }

        var type = new GoldpathCampaignType(key);
        var builder = new GoldpathCampaignTypeBuilder<TTarget>(type);
        configure(builder);
        builder.Bake();
        TypeMap[key] = type;
        return this;
    }

    /// <summary>Finds a type or fails with a teaching message.</summary>
    public GoldpathCampaignType Type(string key)
        => TypeMap.TryGetValue(key, out var type)
            ? type
            : throw new InvalidOperationException(
                $"No campaign type named '{key}' — registered: {string.Join(", ", TypeMap.Keys)}.");
}
