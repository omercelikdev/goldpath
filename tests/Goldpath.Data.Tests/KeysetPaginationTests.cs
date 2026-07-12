using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Goldpath.Tests;

public sealed class KeysetPaginationTests : IDisposable
{
    private sealed class Order
    {
        public long Id { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string Reference { get; set; } = "";
    }

    private sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            // SQLite cannot ORDER BY DateTimeOffset; store as UTC ticks (order-preserving).
            // Real providers (postgres/sqlserver/oracle) need no such mapping.
            => modelBuilder.Entity<Order>()
                .Property(o => o.CreatedAt)
                .HasConversion(static v => v.UtcTicks, static v => new DateTimeOffset(v, TimeSpan.Zero));
    }

    private readonly SqliteConnection _connection;
    private readonly TestDb _db;

    public KeysetPaginationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new TestDb(new DbContextOptionsBuilder<TestDb>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        // 25 rows; timestamps deliberately duplicated in groups of 5 to exercise the tiebreaker.
        var baseTime = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 1; i <= 25; i++)
        {
            _db.Orders.Add(new Order
            {
                Id = i,
                CreatedAt = baseTime.AddMinutes(i / 5),
                Reference = $"ord-{i:d3}",
            });
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Single_key_walk_visits_every_row_exactly_once()
    {
        var seen = new List<long>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var page = await _db.Orders.ToPageAsync(new PageRequest(cursor, 10), o => o.Id, SortDirection.Ascending, CancellationToken.None);
            seen.AddRange(page.Items.Select(o => o.Id));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(3, pages);                                  // 10 + 10 + 5
        Assert.Equal(Enumerable.Range(1, 25).Select(i => (long)i), seen);
    }

    [Fact]
    public async Task Composite_key_walk_handles_duplicate_first_keys()
    {
        var seen = new List<long>();
        string? cursor = null;

        do
        {
            var page = await _db.Orders.ToPageAsync(
                new PageRequest(cursor, 7), o => o.CreatedAt, o => o.Id);
            seen.AddRange(page.Items.Select(o => o.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(25, seen.Count);
        Assert.Equal(seen.Distinct().Count(), seen.Count);        // no duplicates, no skips
    }

    [Fact]
    public async Task Descending_walk_orders_correctly()
    {
        var page = await _db.Orders.ToPageAsync(
            new PageRequest(Size: 5), o => o.Id, SortDirection.Descending, CancellationToken.None);

        Assert.Equal([25L, 24L, 23L, 22L, 21L], page.Items.Select(o => o.Id));
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public async Task Last_page_has_null_cursor()
    {
        var page = await _db.Orders.ToPageAsync(new PageRequest(Size: 200), o => o.Id, SortDirection.Ascending, CancellationToken.None);
        Assert.Equal(25, page.Items.Count);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Invalid_cursor_throws_400_mappable_exception()
    {
        await Assert.ThrowsAsync<GoldpathInvalidCursorException>(
            () => _db.Orders.ToPageAsync(new PageRequest("tampered!!", 10), o => o.Id, SortDirection.Ascending, CancellationToken.None));
    }

    [Fact]
    public async Task Projection_before_pagination_is_supported()
    {
        var page = await _db.Orders
            .Select(o => new { o.Reference, o.Id })
            .ToPageAsync(new PageRequest(Size: 3), o => o.Id, SortDirection.Ascending, CancellationToken.None);

        Assert.Equal(["ord-001", "ord-002", "ord-003"], page.Items.Select(x => x.Reference));
    }
}
