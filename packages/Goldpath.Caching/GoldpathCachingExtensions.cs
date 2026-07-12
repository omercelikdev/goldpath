using Mediant.Behaviors.DependencyInjection;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>Tuning surface — bound from <c>Goldpath:DistributedCaching</c>.</summary>
public sealed class GoldpathCachingOptions
{
    /// <summary>
    /// Cache tiers: <c>["l1"]</c> = in-process only; <c>["l1","l2"]</c> adds Redis.
    /// <see langword="null"/> means the default (both tiers).
    /// </summary>
    public string[]? Levels { get; set; }

    /// <summary>Connection-string name resolved for the L2 (Redis) tier.</summary>
    public string RedisConnectionName { get; set; } = "redis";

    /// <summary>Default TTL (seconds) for both surfaces; per-entry overrides in code.</summary>
    public int DefaultTtlSeconds { get; set; } = 300;

    internal bool HasL2
        => Levels is null || Levels.Contains("l2", StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Registers Ring B caching: Microsoft <see cref="HybridCache"/> as the app surface and
/// Mediant's <c>[Cacheable]</c>/<c>[InvalidatesCache]</c> behaviors as the query surface —
/// composed, not wrapped (ADR-0003). Both share the L2 store and the default TTL; keys go
/// through the tenant-scoped <see cref="GoldpathCacheKeys"/> convention.
/// </summary>
public static class GoldpathCachingExtensions
{
    /// <summary>Adds HybridCache (L1 + optional Redis L2), Mediant caching, and the key helper.</summary>
    public static TBuilder AddGoldpathCaching<TBuilder>(this TBuilder builder, Action<GoldpathCachingOptions>? configure = null)
        where TBuilder : IHostApplicationBuilder
    {
        var options = new GoldpathCachingOptions();
        builder.Configuration.GetSection("Goldpath:DistributedCaching").Bind(options);
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        if (options.HasL2)
        {
            // Null connection string stays registration-safe (docgen/build-time paths);
            // it fails at first use with the provider's own error.
            var connectionString = builder.Configuration.GetConnectionString(options.RedisConnectionName);
            builder.Services.AddStackExchangeRedisCache(redis => redis.Configuration = connectionString);
        }
        else
        {
            // L1-only: HybridCache's secondary tier stays in-process (and the idempotency
            // store keeps an IDistributedCache to talk to).
            builder.Services.AddDistributedMemoryCache();
        }

        var ttl = TimeSpan.FromSeconds(options.DefaultTtlSeconds);
        builder.Services.AddHybridCache(hybrid => hybrid.DefaultEntryOptions = new HybridCacheEntryOptions
        {
            Expiration = ttl,
            LocalCacheExpiration = ttl,
        });

        // Mediant 1.2.0 (issues #130/#131, filed by this module's RFC): the query path now
        // rides the SAME HybridCache — L1+L2 on [Cacheable] hits, and [InvalidatesCache]
        // really invalidates (tag-based, O(1)). Both surfaces, one store, one semantics.
        builder.Services.AddMediantHybridCaching(mediant => mediant.DefaultDurationSeconds = options.DefaultTtlSeconds);
        builder.Services.AddScoped<GoldpathCacheKeys>();
        return builder;
    }
}
