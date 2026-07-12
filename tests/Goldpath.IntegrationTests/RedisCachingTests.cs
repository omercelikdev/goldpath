using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.Redis;
using Xunit;

namespace Goldpath.Tests.Integration;

/// <summary>
/// The L2 tier against REAL Redis: proves the manifest-driven wiring produces a genuinely
/// distributed cache — a value written through one host is served from L2 by a second host
/// (fresh L1), which no in-process proxy can prove.
/// </summary>
public sealed class RedisCachingTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public async Task InitializeAsync() => await _redis.StartAsync();

    public async Task DisposeAsync() => await _redis.DisposeAsync();

    private IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:redis"] = _redis.GetConnectionString();
        builder.AddGoldpathCaching();                     // default levels: [l1, l2]
        return builder.Build();
    }

    [Fact]
    public async Task A_second_host_with_a_cold_l1_reads_the_value_from_redis_l2()
    {
        var key = GoldpathCacheKeys.Compose("tenant-a", "rates", "current");
        var upstreamCalls = 0;

        ValueTask<string> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref upstreamCalls);
            return ValueTask.FromResult("rates-from-upstream");
        }

        using (var hostA = BuildHost())
        {
            var cacheA = hostA.Services.GetRequiredService<HybridCache>();
            Assert.Equal("rates-from-upstream", await cacheA.GetOrCreateAsync(key, Factory));

            // HybridCache writes the L2 tier asynchronously — wait until the entry is
            // visible in Redis before tearing the writing host down.
            var l2 = hostA.Services.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
            for (var i = 0; i < 50 && await l2.GetAsync(key) is null; i++)
            {
                await Task.Delay(100);
            }

            Assert.NotNull(await l2.GetAsync(key));
        }

        using var hostB = BuildHost();               // separate process-equivalent: empty L1
        var cacheB = hostB.Services.GetRequiredService<HybridCache>();
        var value = await cacheB.GetOrCreateAsync(key, Factory);

        Assert.Equal("rates-from-upstream", value);
        Assert.Equal(1, upstreamCalls);              // L2 hit — upstream was never called again
    }

    [Fact]
    public async Task Tenant_isolation_holds_on_the_shared_redis_instance()
    {
        using var host = BuildHost();
        var cache = host.Services.GetRequiredService<HybridCache>();

        await cache.SetAsync(GoldpathCacheKeys.Compose("tenant-a", "limits", "daily"), "1000");
        await cache.SetAsync(GoldpathCacheKeys.Compose("tenant-b", "limits", "daily"), "9999");

        var forA = await cache.GetOrCreateAsync(
            GoldpathCacheKeys.Compose("tenant-a", "limits", "daily"), _ => ValueTask.FromResult("MISS"));
        var forB = await cache.GetOrCreateAsync(
            GoldpathCacheKeys.Compose("tenant-b", "limits", "daily"), _ => ValueTask.FromResult("MISS"));

        Assert.Equal("1000", forA);
        Assert.Equal("9999", forB);
    }
}
