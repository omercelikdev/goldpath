using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace Goldpath.Tests.Integration;

/// <summary>
/// The archival lifecycle on real PostgreSQL: archive-moves-graph, retrieval, a raw-SQL
/// tamper caught by verification, legal hold stopping the purge, and row retention — the
/// exact story the RFC promises, against the real provider.
/// </summary>
public sealed class ArchivalTests : IAsyncLifetime
{
    public sealed class Policy
    {
        public Guid Id { get; set; }
        public string Holder { get; set; } = "";
        public DateTimeOffset? CancelledAt { get; set; }
        public List<Endorsement> Endorsements { get; set; } = [];
    }

    public sealed class Endorsement
    {
        public Guid Id { get; set; }
        public Guid PolicyId { get; set; }
        public string Change { get; set; } = "";
    }

    public sealed class CallRecord
    {
        public long Id { get; set; }
        public DateTimeOffset RecordedAt { get; set; }
        public bool RolledUp { get; set; }
    }

    public sealed class ArchDb(DbContextOptions<ArchDb> options) : DbContext(options)
    {
        public DbSet<Policy> Policies => Set<Policy>();
        public DbSet<CallRecord> Calls => Set<CallRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Policy>().Property(p => p.Id).ValueGeneratedNever();
            modelBuilder.Entity<Endorsement>().Property(e => e.Id).ValueGeneratedNever();
            modelBuilder.Entity<Policy>().HasMany(p => p.Endorsements).WithOne()
                .HasForeignKey(e => e.PolicyId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.AddGoldpathArchiveModel();
        }
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var services = new ServiceCollection();
        services.AddDbContext<ArchDb>(o => o.UseNpgsql(_postgres.GetConnectionString()));
        // The REAL DataProtection module: classify once, the archive redacts on erasure.
        var dataProtection = new GoldpathDataProtectionOptions();
        dataProtection.Catalog(c => c.Classify<Policy>(p => p.Holder, GoldpathDataClass.Personal));
        services.AddRedaction();
        services.AddSingleton<IGoldpathDataProtector>(sp =>
            new GoldpathDataProtector(dataProtection, sp.GetRequiredService<IRedactorProvider>()));
        _services = services.BuildServiceProvider();
        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ArchDb>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private static GoldpathArchivalOptions PolicyArchival(int retainYears = 10)
    {
        var options = new GoldpathArchivalOptions();
        options.AddArchive<Policy>(a => a
            .Graph(p => p.Endorsements)
            .Key(p => p.Id)
            .DueWhen(p => p.CancelledAt != null, p => p.CancelledAt!.Value)
            .ArchiveAfter(TimeSpan.FromDays(30))
            .RetainFor(retainYears)
            .DeleteHotRowsAfterArchive());
        options.AddRowRetention<CallRecord>(r => r
            .After(TimeSpan.FromDays(90), c => c.RecordedAt)
            .Where(c => c.RolledUp));
        return options;
    }

    [Fact]
    public async Task The_full_lifecycle_holds_on_postgres()
    {
        var engine = new GoldpathArchiveEngine<ArchDb>(TimeProvider.System, NullLogger<GoldpathArchiveEngine<ArchDb>>.Instance);
        var options = PolicyArchival(retainYears: 0);
        var definition = options.Archives[0];

        // Seed: three cancelled policies with endorsements + purgeable/guarded call records.
        var keys = new List<string>();
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArchDb>();
            for (var i = 0; i < 3; i++)
            {
                var policy = new Policy
                {
                    Id = Guid.NewGuid(),
                    Holder = $"holder-{i}",
                    CancelledAt = DateTimeOffset.UtcNow.AddDays(-90),
                    Endorsements = [new Endorsement { Id = Guid.NewGuid(), Change = $"change-{i}" }],
                };
                db.Policies.Add(policy);
                keys.Add(policy.Id.ToString());
            }

            for (var i = 0; i < 50; i++)
            {
                db.Calls.Add(new CallRecord { RecordedAt = DateTimeOffset.UtcNow.AddDays(-200), RolledUp = i % 2 == 0 });
            }

            await db.SaveChangesAsync();
        }

        // ARCHIVE: graphs move, chain seals.
        using (var scope = _services.CreateScope())
        {
            Assert.Equal(3, await engine.ArchiveNextBatchAsync(scope.ServiceProvider, definition, 10, CancellationToken.None));
            var db = scope.ServiceProvider.GetRequiredService<ArchDb>();
            Assert.Equal(0, await db.Policies.CountAsync());
            Assert.Equal(3, await db.Set<GoldpathArchiveEntry>().CountAsync());
        }

        // RETRIEVE: the finance budget's code path (indexed lookup).
        using (var scope = _services.CreateScope())
        {
            var entry = await engine.RetrieveAsync(scope.ServiceProvider, "Policy", keys[1], CancellationToken.None);
            Assert.NotNull(entry);
            Assert.Contains("change-1", entry.Document, StringComparison.Ordinal);
        }

        // TAMPER via raw SQL (the hostile-DBA scenario) → verification catches it.
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArchDb>();
            // Name-independent tamper (archive order is Guid order — never bet on which
            // holder landed at chain-1): append a byte, guaranteed content change.
            var affected = await db.Database.ExecuteSqlRawAsync(
                """UPDATE "GoldpathArchiveEntries" SET "Document" = "Document" || ' ' WHERE "ChainIndex" = 1""");
            Assert.Equal(1, affected);
            var findings = await engine.VerifySliceAsync(scope.ServiceProvider, definition, 1, 3, CancellationToken.None);
            var finding = Assert.Single(findings, f => f.ChainIndex == 1);
            Assert.Contains("content hash", finding.Problem, StringComparison.Ordinal);

            // repair the tamper so the purge phase below starts from a clean chain
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE "GoldpathArchiveEntries" SET "Document" = rtrim("Document") WHERE "ChainIndex" = 1""");
            Assert.Empty(await engine.VerifySliceAsync(scope.ServiceProvider, definition, 1, 3, CancellationToken.None));
        }

        // LEGAL HOLD stops the purge at the held entry; lifting releases it.
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArchDb>();
            // Archive order is KEY order (Guid), not insertion order — hold the chain's middle.
            var heldKey = await db.Set<GoldpathArchiveEntry>().Where(e => e.ChainIndex == 2).Select(e => e.AggregateKey).SingleAsync();
            db.Add(new GoldpathLegalHold { Definition = "Policy", AggregateKey = heldKey, CaseReference = "LIT-9", PlacedBy = "counsel", PlacedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            Assert.Equal(1, await engine.PurgeExpiredEntriesAsync(scope.ServiceProvider, definition, 10, CancellationToken.None));
            Assert.Equal(2, await db.Set<GoldpathArchiveEntry>().CountAsync());
        }

        // ERASURE (KVKK): the classified field redacts, the chain STILL verifies, the
        // evidence row answers the authority.
        using (var scope = _services.CreateScope())
        {
            var admin = new GoldpathArchiveAdminService<ArchDb>(
                engine, options, _services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
            var db = scope.ServiceProvider.GetRequiredService<ArchDb>();
            // The middle entry still carries the (lifted-later) hold story; erase the tail.
            var erasableKey = await db.Set<GoldpathArchiveEntry>()
                .OrderByDescending(e => e.ChainIndex).Select(e => e.AggregateKey).FirstAsync();

            var erased = await admin.EraseAsync("Policy", erasableKey, "citizen-42", "dpo", "KVKK-1", CancellationToken.None);
            Assert.True(erased.Ok, erased.Message);

            var entry = await db.Set<GoldpathArchiveEntry>().AsNoTracking().SingleAsync(e => e.AggregateKey == erasableKey);
            Assert.NotNull(entry.ErasedAt);
            Assert.DoesNotContain("holder-", entry.Document, StringComparison.Ordinal);
            Assert.Empty(await admin.VerifyAsync("Policy", CancellationToken.None));   // lawful divergence
            Assert.Single(await admin.GetErasuresAsync(10, CancellationToken.None));
        }

        // ROW RETENTION: only guarded, aged rows leave.
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArchDb>();
            var retention = options.RowRetentions[0];
            Assert.Equal(25, await retention.CountDueAsync(db, DateTimeOffset.UtcNow, CancellationToken.None));
            Assert.Equal(25, await retention.PurgeBatchAsync(db, DateTimeOffset.UtcNow, 100, CancellationToken.None));
            Assert.Equal(25, await db.Calls.CountAsync());   // the unguarded half survives
        }
    }
}
