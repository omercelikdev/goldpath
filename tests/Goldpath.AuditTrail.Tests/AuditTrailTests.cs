using Mediant.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

public sealed class AuditTrailTests : IDisposable
{
    private sealed class Loan : IAuditedEntity, IAuditLogged
    {
        public long Id { get; set; }
        public string Applicant { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTimeOffset? ModifiedAt { get; set; }
        public string? ModifiedBy { get; set; }
    }

    private sealed class StampOnly : IAuditedEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTimeOffset? ModifiedAt { get; set; }
        public string? ModifiedBy { get; set; }
    }

    private sealed class LoanDb(DbContextOptions<LoanDb> options) : DbContext(options)
    {
        public DbSet<Loan> Loans => Set<Loan>();
        public DbSet<StampOnly> Stamps => Set<StampOnly>();
        public DbSet<GoldpathAuditLogEntry> AuditLog => Set<GoldpathAuditLogEntry>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.AddGoldpathAuditLog();
    }

    private sealed class FixedUser : IUserContext
    {
        public string? UserId => "auditor-7";
    }

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private IHost _host = null!;

    public AuditTrailTests()
    {
        _connection.Open();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IUserContext, FixedUser>();
        builder.AddGoldpathAuditTrail<HostApplicationBuilder, LoanDb>();
        builder.AddGoldpathData<HostApplicationBuilder, LoanDb>(o => o.UseSqlite(_connection));
        _host = builder.Build();

        using var scope = _host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<LoanDb>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _host.Dispose();
        _connection.Dispose();
    }

    private IServiceScope NewScope() => _host.Services.CreateScope();

    [Fact]
    public async Task Add_modify_delete_produce_correct_change_rows_with_who_and_values()
    {
        long id;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LoanDb>();
            var loan = new Loan { Applicant = "acme", Amount = 100m };
            db.Loans.Add(loan);
            await db.SaveChangesAsync();
            id = loan.Id;

            loan.Amount = 250m;
            await db.SaveChangesAsync();

            db.Loans.Remove(loan);
            await db.SaveChangesAsync();
        }

        using var verify = NewScope();
        var log = await verify.ServiceProvider.GetRequiredService<LoanDb>()
            .AuditLog.Where(e => e.EntityType == nameof(Loan)).ToListAsync();

        // Added: a row per property, old null.
        var added = log.Where(e => e.Action == "Added").ToList();
        Assert.Contains(added, e => e.PropertyName == nameof(Loan.Amount) && e.OldValue == null && e.NewValue == "100");
        Assert.All(added, e => Assert.Equal("auditor-7", e.User));

        // Modified: ONLY the changed property, old -> new.
        var modified = log.Where(e => e.Action == "Modified").ToList();
        var amountChange = Assert.Single(modified);
        Assert.Equal(nameof(Loan.Amount), amountChange.PropertyName);
        Assert.Equal("100", amountChange.OldValue);
        Assert.Equal("250", amountChange.NewValue);
        Assert.Equal(id.ToString(), amountChange.EntityKey);

        // Deleted: old values, new null.
        Assert.Contains(log, e => e.Action == "Deleted" && e.PropertyName == nameof(Loan.Applicant)
            && e.OldValue == "acme" && e.NewValue == null);
    }

    [Fact]
    public async Task Stamps_are_filled_automatically_and_stamponly_entities_get_no_rows()
    {
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LoanDb>();
            db.Stamps.Add(new StampOnly { Name = "s1" });
            await db.SaveChangesAsync();
        }

        using var verify = NewScope();
        var db2 = verify.ServiceProvider.GetRequiredService<LoanDb>();
        var stamped = await db2.Stamps.SingleAsync();

        Assert.Equal("auditor-7", stamped.CreatedBy);
        Assert.True(stamped.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Null(stamped.ModifiedAt);
        Assert.Equal(0, await db2.AuditLog.CountAsync(e => e.EntityType == nameof(StampOnly)));   // D2: stamps only
    }

    [Fact]
    public async Task Rolled_back_transaction_leaves_no_audit_rows()
    {
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LoanDb>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Loans.Add(new Loan { Applicant = "doomed", Amount = 1m });
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();          // D1: change + audit die together
        }

        using var verify = NewScope();
        var db2 = verify.ServiceProvider.GetRequiredService<LoanDb>();
        Assert.Equal(0, await db2.Loans.CountAsync(l => l.Applicant == "doomed"));
        Assert.Equal(0, await db2.AuditLog.CountAsync(e => e.NewValue == "doomed"));
    }

    [Fact]
    public async Task NamesOnly_mode_records_properties_without_values()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IUserContext, FixedUser>();
        builder.AddGoldpathAuditTrail<HostApplicationBuilder, LoanDb>(o => o.EntityValues = AuditValueMode.NamesOnly);
        builder.AddGoldpathData<HostApplicationBuilder, LoanDb>(o => o.UseSqlite(connection));
        using var host = builder.Build();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoanDb>();
        await db.Database.EnsureCreatedAsync();
        db.Loans.Add(new Loan { Applicant = "secret-corp", Amount = 999m });
        await db.SaveChangesAsync();

        var rows = await db.AuditLog.Where(e => e.EntityType == nameof(Loan)).ToListAsync();
        Assert.NotEmpty(rows);
        Assert.All(rows, e => Assert.Null(e.NewValue));
        Assert.DoesNotContain(rows, e => e.OldValue == "secret-corp" || e.NewValue == "secret-corp");
    }

    [Fact]
    public void Command_level_store_is_registered_and_resolvable()
    {
        using var scope = NewScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IAuditStore>());   // Mediant EfAuditStore composed
    }
}
