using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace Goldpath.Tests.Integration;

/// <summary>
/// THE lock contract on real stores (Locking RFC §7): the same assertions run against
/// Redis (RedLock) and Postgres (advisory locks) — proof that the manifest's provider
/// swap is behavior-neutral. Wiring goes through AddGoldpathLocking, not hand-built providers.
/// </summary>
public sealed class LockContractTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
        => await Task.WhenAll(_redis.StartAsync(), _postgres.StartAsync());

    public async Task DisposeAsync()
    {
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private IHost BuildHost(GoldpathLockProvider provider)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:locks"] = provider == GoldpathLockProvider.Redis
            ? _redis.GetConnectionString()
            : _postgres.GetConnectionString();
        builder.AddGoldpathLocking(o =>
        {
            o.Provider = provider;
            o.ConnectionName = "locks";
        });
        return builder.Build();
    }

    [Theory]
    [InlineData(GoldpathLockProvider.Redis)]
    [InlineData(GoldpathLockProvider.Postgres)]
    public async Task Exactly_one_competitor_holds_the_lock_and_release_hands_it_over(GoldpathLockProvider provider)
    {
        using var host = BuildHost(provider);
        var locks = host.Services.GetRequiredService<IDistributedLockProvider>();
        var name = GoldpathLockNames.Global($"contract-{provider}".ToLowerInvariant());

        // Two competitors: exactly one wins.
        var first = await locks.TryAcquireLockAsync(name, TimeSpan.FromSeconds(2));
        Assert.NotNull(first);
        var loser = await locks.TryAcquireLockAsync(name, TimeSpan.FromSeconds(2));
        Assert.Null(loser);                                     // timeout → null, never a throw

        // Release hands the lock over.
        await first.DisposeAsync();
        var second = await locks.TryAcquireLockAsync(name, TimeSpan.FromSeconds(5));
        Assert.NotNull(second);
        await second.DisposeAsync();
    }

    [Theory]
    [InlineData(GoldpathLockProvider.Redis)]
    [InlineData(GoldpathLockProvider.Postgres)]
    public async Task Tenant_scoped_names_do_not_contend_across_tenants(GoldpathLockProvider provider)
    {
        using var host = BuildHost(provider);
        var locks = host.Services.GetRequiredService<IDistributedLockProvider>();

        string forA, forB;
        using (GoldpathTenant.Use("tenant-a"))
        {
            forA = new GoldpathLockNames().For("nightly-report");
        }

        using (GoldpathTenant.Use("tenant-b"))
        {
            forB = new GoldpathLockNames().For("nightly-report");
        }

        await using var heldByA = await locks.TryAcquireLockAsync(forA, TimeSpan.FromSeconds(2));
        await using var heldByB = await locks.TryAcquireLockAsync(forB, TimeSpan.FromSeconds(2));

        Assert.NotNull(heldByA);
        Assert.NotNull(heldByB);   // same logical job, different tenants — no contention
    }
}
