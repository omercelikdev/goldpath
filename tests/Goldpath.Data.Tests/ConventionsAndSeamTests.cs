using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Xunit;

namespace Goldpath.Tests;

public sealed class ConventionsAndSeamTests : IDisposable
{
    private enum ChequeStatus
    {
        Issued,
        Bounced,
    }

    private sealed class Cheque
    {
        public long Id { get; set; }
        public string Number { get; set; } = "";
        public decimal Amount { get; set; }
        public ChequeStatus Status { get; set; }
        public TenantId TenantId { get; set; }
    }

    private sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
    {
        public DbSet<Cheque> Cheques => Set<Cheque>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
            => configurationBuilder.ApplyGoldpathConventions();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ApplyGoldpathModelDefaults();
    }

    private sealed class RecordingContributor : IEntitySaveContributor
    {
        public List<(string State, GoldpathSaveContext Context)> Calls { get; } = [];

        public void OnSaving(EntityEntry entry, GoldpathSaveContext context)
            => Calls.Add((entry.State.ToString(), context));
    }

    private readonly SqliteConnection _connection;

    public ConventionsAndSeamTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private TestDb CreateDb(params IEntitySaveContributor[] contributors)
    {
        var options = new DbContextOptionsBuilder<TestDb>()
            .UseSqlite(_connection)
            .AddInterceptors(new GoldpathSaveChangesInterceptor(
                contributors,
                new GoldpathSaveContext(TimeProvider.System, TenantId.Create("acme"))))
            .Options;

        var db = new TestDb(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public void Conventions_apply_length_precision_enum_and_tenant_conversion()
    {
        using var db = CreateDb();
        var entity = db.Model.FindEntityType(typeof(Cheque))!;

        Assert.Equal(256, entity.FindProperty(nameof(Cheque.Number))!.GetMaxLength());
        Assert.Equal(18, entity.FindProperty(nameof(Cheque.Amount))!.GetPrecision());
        Assert.Equal(4, entity.FindProperty(nameof(Cheque.Amount))!.GetScale());
        Assert.Equal(typeof(string), entity.FindProperty(nameof(Cheque.Status))!.GetProviderClrType());
        Assert.Equal(TenantId.MaxLength, entity.FindProperty(nameof(Cheque.TenantId))!.GetMaxLength());
    }

    [Fact]
    public async Task TenantId_round_trips_through_conversion()
    {
        using (var db = CreateDb())
        {
            db.Cheques.Add(new Cheque { Number = "chq-1", Amount = 150.25m, TenantId = TenantId.Create("acme-bank") });
            await db.SaveChangesAsync();
        }

        using var reader = CreateDb();
        var loaded = await reader.Cheques.SingleAsync(c => c.Number == "chq-1");
        Assert.Equal("acme-bank", loaded.TenantId.Value);
        Assert.Equal(150.25m, loaded.Amount);
    }

    [Fact]
    public async Task Contributors_run_for_added_modified_deleted_with_context()
    {
        var contributor = new RecordingContributor();
        using var db = CreateDb(contributor);

        var cheque = new Cheque { Number = "chq-2", Amount = 1m, TenantId = TenantId.Create("acme") };
        db.Cheques.Add(cheque);
        await db.SaveChangesAsync();

        cheque.Amount = 2m;
        await db.SaveChangesAsync();

        db.Cheques.Remove(cheque);
        await db.SaveChangesAsync();

        Assert.Equal(["Added", "Modified", "Deleted"], contributor.Calls.Select(c => c.State));
        Assert.All(contributor.Calls, c => Assert.Equal("acme", c.Context.Tenant!.Value.Value));
    }
}
