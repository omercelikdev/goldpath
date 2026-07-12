using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace Goldpath.Tests.Integration;

/// <summary>
/// Archival RFC §6 performance proofs (scripts/bench-archival.sh → ops/archival-benchmarks.md).
/// Trait-gated out of the normal suite: evidence, not regression tests.
/// </summary>
[Trait("Category", "Bench")]
public sealed class ArchivalBenchTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var services = new ServiceCollection();
        services.AddDbContext<ArchivalTests.ArchDb>(o => o.UseNpgsql(_postgres.GetConnectionString()));
        _services = services.BuildServiceProvider();
        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ArchivalTests.ArchDb>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Bench_recall_p95_at_one_million_entries()
    {
        // Seed 1M entries with pg-native generate_series (EF would take minutes).
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArchivalTests.ArchDb>();
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "GoldpathArchiveEntries"
                    ("Definition", "AggregateKey", "Tenant", "Document", "SchemaVersion", "DueAt", "ArchivedAt", "ChainIndex", "ContentHash", "ChainHash", "PreviousHash", "ErasedAt")
                SELECT 'Policy', 'key-' || s, NULL, '{{"seed":' || s || '}}', 1, now(), now(), s, md5(s::text), md5(s::text), '', NULL
                FROM generate_series(1, 1000000) AS s
                """);
        }

        var engine = new GoldpathArchiveEngine<ArchivalTests.ArchDb>(
            TimeProvider.System, NullLogger<GoldpathArchiveEngine<ArchivalTests.ArchDb>>.Instance);
        var samples = new List<double>();
        var random = new Random(42);
        for (var i = 0; i < 200; i++)
        {
            using var scope = _services.CreateScope();
            var key = $"key-{random.Next(1, 1_000_001)}";
            var start = Stopwatch.GetTimestamp();
            var entry = await engine.RetrieveAsync(scope.ServiceProvider, "Policy", key, CancellationToken.None);
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            Assert.NotNull(entry);
        }

        samples.Sort();
        var p95 = samples[(int)(samples.Count * 0.95)];
        Console.WriteLine($"BENCH-ARCHIVAL recall@1M p95={p95:F1}ms (budget 5000ms)");
        Assert.True(p95 < 5000, $"recall p95 {p95:F1}ms exceeds the finance card's 5s budget");
    }

    [Fact]
    public async Task Bench_archive_round_trip_and_row_purge()
    {
        var engine = new GoldpathArchiveEngine<ArchivalTests.ArchDb>(
            TimeProvider.System, NullLogger<GoldpathArchiveEngine<ArchivalTests.ArchDb>>.Instance);
        var options = new GoldpathArchivalOptions();
        options.AddArchive<ArchivalTests.Policy>(a => a
            .Graph(p => p.Endorsements)
            .Key(p => p.Id)
            .DueWhen(p => p.CancelledAt != null, p => p.CancelledAt!.Value)
            .DeleteHotRowsAfterArchive());
        options.AddRowRetention<ArchivalTests.CallRecord>(r => r
            .After(TimeSpan.FromDays(1), c => c.RecordedAt)
            .Where(c => c.RolledUp));

        // Round-trip: a claim-sized graph (50 children) extract → store → retrieve.
        Guid policyId;
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArchivalTests.ArchDb>();
            var policy = new ArchivalTests.Policy { Id = Guid.NewGuid(), Holder = "bench", CancelledAt = DateTimeOffset.UtcNow.AddDays(-1) };
            for (var i = 0; i < 50; i++)
            {
                policy.Endorsements.Add(new ArchivalTests.Endorsement { Id = Guid.NewGuid(), Change = $"change-{i}" });
            }

            db.Policies.Add(policy);
            await db.SaveChangesAsync();
            policyId = policy.Id;
        }

        var watch = Stopwatch.StartNew();
        using (var scope = _services.CreateScope())
        {
            await engine.ArchiveNextBatchAsync(scope.ServiceProvider, options.Archives[0], 10, CancellationToken.None);
            var entry = await engine.RetrieveAsync(scope.ServiceProvider, "Policy", policyId.ToString(), CancellationToken.None);
            Assert.NotNull(entry);
        }

        watch.Stop();
        Console.WriteLine($"BENCH-ARCHIVAL round-trip(50-row graph)={watch.Elapsed.TotalSeconds:F2}s (budget 10s)");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));

        // Row purge: 100k aged rows in bounded batches.
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArchivalTests.ArchDb>();
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Calls" ("RecordedAt", "RolledUp")
                SELECT now() - interval '200 days', TRUE FROM generate_series(1, 100000)
                """);
        }

        watch.Restart();
        var total = 0;
        while (true)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ArchivalTests.ArchDb>();
            var purged = await options.RowRetentions[0].PurgeBatchAsync(db, DateTimeOffset.UtcNow, 2000, CancellationToken.None);
            if (purged == 0)
            {
                break;
            }

            total += purged;
        }

        watch.Stop();
        Console.WriteLine($"BENCH-ARCHIVAL purge100k={watch.Elapsed.TotalSeconds:F1}s rows={total} (budget 300s)");
        Assert.Equal(100_000, total);
        Assert.True(watch.Elapsed < TimeSpan.FromMinutes(5));
    }
}
