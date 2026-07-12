using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

/// <summary>
/// Tests added to kill surviving mutants from the first Stryker run (score 65.67%):
/// the DI extension path, descending cursor continuation, mixed-direction composite walks,
/// and the convention fallback branches.
/// </summary>
public sealed class MutationHardeningTests : IDisposable
{
    private enum Kind
    {
        Alpha,
        Beta,
    }

    private sealed class Item
    {
        public long Id { get; set; }
        public long Bucket { get; set; }
        public Kind Kind { get; set; }
        public Kind? OptionalKind { get; set; }
        public Kind Sized { get; set; }
        public Kind Custom { get; set; }
    }

    private sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Item>().Property(i => i.Sized).HasMaxLength(16);
            modelBuilder.Entity<Item>().Property(i => i.Custom).HasConversion<int>();
            modelBuilder.ApplyGoldpathModelDefaults();
        }
    }

    private sealed class RecordingContributor : IEntitySaveContributor
    {
        public int Calls { get; private set; }

        public void OnSaving(EntityEntry entry, GoldpathSaveContext context) => Calls++;
    }

    private readonly SqliteConnection _connection;

    public MutationHardeningTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private TestDb CreateDb()
    {
        var db = new TestDb(new DbContextOptionsBuilder<TestDb>().UseSqlite(_connection).Options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task AddGoldpathData_wires_interceptor_timeprovider_and_contributors()
    {
        var contributor = new RecordingContributor();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IEntitySaveContributor>(contributor);
        builder.AddGoldpathData<HostApplicationBuilder, TestDb>(o => o.UseSqlite(_connection));

        using var host = builder.Build();
        Assert.Same(TimeProvider.System, host.Services.GetRequiredService<TimeProvider>());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDb>();
        await db.Database.EnsureCreatedAsync();
        db.Items.Add(new Item { Id = 1 });
        await db.SaveChangesAsync();

        Assert.Equal(1, contributor.Calls);   // interceptor attached via the extension, not manually
    }

    [Fact]
    public async Task Descending_walk_with_cursor_continuation_is_exact()
    {
        using var db = CreateDb();
        for (var i = 1; i <= 25; i++)
        {
            db.Items.Add(new Item { Id = i, Bucket = 0 });
        }

        await db.SaveChangesAsync();

        var seen = new List<long>();
        string? cursor = null;
        do
        {
            var page = await db.Items.ToPageAsync(
                new PageRequest(cursor, 10), i => i.Id, SortDirection.Descending, CancellationToken.None);
            seen.AddRange(page.Items.Select(i => i.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(Enumerable.Range(1, 25).Reverse().Select(i => (long)i), seen);
    }

    [Fact]
    public async Task Mixed_direction_composite_walk_is_exact()
    {
        using var db = CreateDb();
        for (var i = 1; i <= 20; i++)
        {
            db.Items.Add(new Item { Id = i, Bucket = i % 3 });
        }

        await db.SaveChangesAsync();

        var seen = new List<(long, long)>();
        string? cursor = null;
        do
        {
            // Bucket DESCENDING, id ASCENDING within each bucket.
            var page = await db.Items.ToPageAsync(
                new PageRequest(cursor, 7), i => i.Bucket, i => i.Id,
                SortDirection.Descending, SortDirection.Ascending, CancellationToken.None);
            seen.AddRange(page.Items.Select(i => (i.Bucket, i.Id)));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        var expected = Enumerable.Range(1, 20)
            .Select(i => (Bucket: (long)(i % 3), Id: (long)i))
            .OrderByDescending(x => x.Bucket).ThenBy(x => x.Id)
            .Select(x => (x.Bucket, x.Id));
        Assert.Equal(expected, seen);
    }

    [Fact]
    public void Enum_convention_fallbacks_behave_exactly()
    {
        using var db = CreateDb();
        var entity = db.Model.FindEntityType(typeof(Item))!;

        // Plain enum → string, default max length 64.
        Assert.Equal(typeof(string), entity.FindProperty(nameof(Item.Kind))!.GetProviderClrType());
        Assert.Equal(64, entity.FindProperty(nameof(Item.Kind))!.GetMaxLength());

        // Nullable enum → the underlying-type branch must also convert.
        Assert.Equal(typeof(string), entity.FindProperty(nameof(Item.OptionalKind))!.GetProviderClrType());

        // Explicit max length wins over the 64 fallback.
        Assert.Equal(16, entity.FindProperty(nameof(Item.Sized))!.GetMaxLength());

        // An explicit converter is never overridden by the convention.
        Assert.NotEqual(typeof(string), entity.FindProperty(nameof(Item.Custom))!.GetProviderClrType());
    }
}
