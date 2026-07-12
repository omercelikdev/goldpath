using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Goldpath.Tests.Integration;

/// <summary>
/// The keyset executor against REAL PostgreSQL (deferred-ledger item from the Data RFC):
/// proves the CompareTo-based predicate and DateTimeOffset ordering translate on Npgsql —
/// the class of issue SQLite proxies cannot catch.
/// </summary>
public sealed class PostgresKeysetTests : IAsyncLifetime
{
    private sealed class Row
    {
        public long Id { get; set; }
        public DateTimeOffset StampedAt { get; set; }
        public Guid Correlation { get; set; }
    }

    private sealed class PgDb(DbContextOptions<PgDb> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private PgDb CreateDb()
    {
        var db = new PgDb(new DbContextOptionsBuilder<PgDb>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task Composite_datetimeoffset_walk_translates_and_is_exact_on_postgres()
    {
        using var db = CreateDb();
        var baseTime = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 1; i <= 30; i++)
        {
            db.Rows.Add(new Row
            {
                Id = i,
                StampedAt = baseTime.AddMinutes(i / 4),   // heavy duplicates → tiebreaker pressure
                Correlation = Guid.NewGuid(),
            });
        }

        await db.SaveChangesAsync();

        var seen = new List<long>();
        string? cursor = null;
        do
        {
            var page = await db.Rows.ToPageAsync(
                new PageRequest(cursor, 7), r => r.StampedAt, r => r.Id);
            seen.AddRange(page.Items.Select(r => r.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(Enumerable.Range(1, 30).Select(i => (long)i), seen);   // no skips, no dups, exact order
    }

    [Fact]
    public async Task Descending_guid_single_key_translates_on_postgres()
    {
        using var db = CreateDb();
        for (var i = 1; i <= 9; i++)
        {
            db.Rows.Add(new Row { Id = i, StampedAt = DateTimeOffset.UtcNow, Correlation = Guid.NewGuid() });
        }

        await db.SaveChangesAsync();

        // Guid CompareTo must translate (uuid ordering) — single key, descending, with cursor walk.
        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await db.Rows.ToPageAsync(
                new PageRequest(cursor, 4), r => r.Correlation, SortDirection.Descending, CancellationToken.None);
            seen.AddRange(page.Items.Select(r => r.Correlation));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(9, seen.Count);
        Assert.Equal(seen.OrderByDescending(g => g), seen);
        Assert.Equal(seen.Distinct().Count(), seen.Count);
    }

    [Fact]
    public async Task Member_init_projection_walk_translates_on_postgres()
    {
        using var db = CreateDb();
        var t0 = new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero);
        for (var i = 1; i <= 12; i++)
        {
            db.Rows.Add(new Row { Id = i, StampedAt = t0.AddSeconds(i), Correlation = Guid.NewGuid() });
        }

        await db.SaveChangesAsync();

        // The documented projection rule: member-init projections translate.
        var page = await db.Rows
            .Select(r => new RowDto { Id = r.Id, StampedAt = r.StampedAt })
            .ToPageAsync(new PageRequest(Size: 5), d => d.StampedAt, d => d.Id);

        Assert.Equal([1L, 2L, 3L, 4L, 5L], page.Items.Select(d => d.Id));
        Assert.NotNull(page.NextCursor);
    }

    private sealed record RowDto
    {
        public long Id { get; init; }
        public DateTimeOffset StampedAt { get; init; }
    }
}
