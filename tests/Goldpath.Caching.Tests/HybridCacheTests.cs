using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

public sealed class HybridCacheTests
{
    private static IHost BuildL1OnlyHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddGoldpathCaching(o => o.Levels = ["l1"]);
        return builder.Build();
    }

    [Fact]
    public async Task L1_only_mode_serves_from_memory_without_any_redis_dependency()
    {
        using var host = BuildL1OnlyHost();
        var cache = host.Services.GetRequiredService<HybridCache>();
        var calls = 0;

        async ValueTask<string> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await Task.Yield();
            return "rates-v1";
        }

        var key = GoldpathCacheKeys.Compose(null, "rates", "current");
        var first = await cache.GetOrCreateAsync(key, Factory);
        var second = await cache.GetOrCreateAsync(key, Factory);

        Assert.Equal("rates-v1", first);
        Assert.Equal("rates-v1", second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Concurrent_misses_stampede_into_a_single_upstream_call()
    {
        using var host = BuildL1OnlyHost();
        var cache = host.Services.GetRequiredService<HybridCache>();
        var calls = 0;
        var gate = new TaskCompletionSource();

        async ValueTask<string> SlowFactory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return "slow-value";
        }

        var key = GoldpathCacheKeys.Compose(null, "rates", "stampede");
        var tasks = Enumerable.Range(0, 16)
            .Select(_ => cache.GetOrCreateAsync(key, SlowFactory).AsTask())
            .ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("slow-value", r));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Tenant_scoped_keys_keep_tenants_fully_isolated()
    {
        using var host = BuildL1OnlyHost();
        var cache = host.Services.GetRequiredService<HybridCache>();

        var forA = await cache.GetOrCreateAsync(
            GoldpathCacheKeys.Compose("tenant-a", "rates", "current"), _ => ValueTask.FromResult("A"));
        var forB = await cache.GetOrCreateAsync(
            GoldpathCacheKeys.Compose("tenant-b", "rates", "current"), _ => ValueTask.FromResult("B"));

        Assert.Equal("A", forA);
        Assert.Equal("B", forB);

        // Evicting one tenant's entry leaves the other's untouched.
        await cache.RemoveAsync(GoldpathCacheKeys.Compose("tenant-a", "rates", "current"));
        var forBAgain = await cache.GetOrCreateAsync(
            GoldpathCacheKeys.Compose("tenant-b", "rates", "current"), _ => ValueTask.FromResult("B2"));
        Assert.Equal("B", forBAgain);
    }
}
