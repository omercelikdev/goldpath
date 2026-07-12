using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

public sealed class SoftDeleteTests : IDisposable
{
    private sealed class Cheque : ISoftDeletable, IAuditLogged
    {
        public long Id { get; set; }
        public string Number { get; set; } = "";
        public bool IsDeleted { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }

    private sealed class ChequeDb(DbContextOptions<ChequeDb> options) : DbContext(options)
    {
        public DbSet<Cheque> Cheques => Set<Cheque>();
        public DbSet<GoldpathAuditLogEntry> AuditLog => Set<GoldpathAuditLogEntry>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddGoldpathAuditLog();
            modelBuilder.ApplyGoldpathSoftDelete();
        }
    }

    private sealed class FixedUser : IUserContext
    {
        public string? UserId => "eraser-1";
    }

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IHost _host;

    public SoftDeleteTests()
    {
        _connection.Open();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IUserContext, FixedUser>();
        builder.AddGoldpathSoftDelete();
        builder.AddGoldpathAuditTrail<HostApplicationBuilder, ChequeDb>();   // interplay under test
        builder.AddGoldpathData<HostApplicationBuilder, ChequeDb>(o => o.UseSqlite(_connection));
        _host = builder.Build();

        using var scope = _host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ChequeDb>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _host.Dispose();
        _connection.Dispose();
    }

    private IServiceScope NewScope() => _host.Services.CreateScope();

    private async Task<long> AddChequeAsync(string number)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ChequeDb>();
        var cheque = new Cheque { Number = number };
        db.Cheques.Add(cheque);
        await db.SaveChangesAsync();
        return cheque.Id;
    }

    [Fact]
    public async Task Remove_becomes_a_stamped_update_and_default_queries_hide_it()
    {
        var id = await AddChequeAsync("chq-1");

        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChequeDb>();
            db.Cheques.Remove(await db.Cheques.SingleAsync(c => c.Id == id));
            await db.SaveChangesAsync();
        }

        using var verify = NewScope();
        var db2 = verify.ServiceProvider.GetRequiredService<ChequeDb>();

        Assert.Equal(0, await db2.Cheques.CountAsync(c => c.Id == id));                     // hidden by default
        var raw = await db2.Cheques.IgnoreQueryFilters().SingleAsync(c => c.Id == id);      // row survived
        Assert.True(raw.IsDeleted);
        Assert.Equal("eraser-1", raw.DeletedBy);
        Assert.NotNull(raw.DeletedAt);
    }

    [Fact]
    public async Task Suppress_scope_really_deletes_and_only_within_the_scope()
    {
        var hardId = await AddChequeAsync("chq-hard");
        var softId = await AddChequeAsync("chq-soft");

        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChequeDb>();
            using (GoldpathSoftDelete.Suppress())
            {
                db.Cheques.Remove(await db.Cheques.SingleAsync(c => c.Id == hardId));
                await db.SaveChangesAsync();                                               // real DELETE
            }

            db.Cheques.Remove(await db.Cheques.SingleAsync(c => c.Id == softId));
            await db.SaveChangesAsync();                                                   // back to soft
        }

        using var verify = NewScope();
        var db2 = verify.ServiceProvider.GetRequiredService<ChequeDb>();
        Assert.Equal(0, await db2.Cheques.IgnoreQueryFilters().CountAsync(c => c.Id == hardId));   // gone
        Assert.Equal(1, await db2.Cheques.IgnoreQueryFilters().CountAsync(c => c.Id == softId));   // survived
    }

    [Fact]
    public async Task Audit_sees_the_soft_delete_as_exactly_three_modified_fields()
    {
        var id = await AddChequeAsync("chq-audit");

        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChequeDb>();
            db.Cheques.Remove(await db.Cheques.SingleAsync(c => c.Id == id));
            await db.SaveChangesAsync();
        }

        using var verify = NewScope();
        var log = await verify.ServiceProvider.GetRequiredService<ChequeDb>()
            .AuditLog.Where(e => e.EntityKey == id.ToString() && e.Action != "Added").ToListAsync();

        Assert.All(log, e => Assert.Equal("Modified", e.Action));                          // never "Deleted"
        Assert.Equal(3, log.Count);                                                        // exactly the 3 fields
        var isDeletedRow = Assert.Single(log, e => e.PropertyName == nameof(ISoftDeletable.IsDeleted));
        Assert.Equal("False", isDeletedRow.OldValue);
        Assert.Equal("True", isDeletedRow.NewValue);
        Assert.Contains(log, e => e.PropertyName == nameof(ISoftDeletable.DeletedBy) && e.NewValue == "eraser-1");
    }

    [Fact]
    public async Task Undelete_is_a_plain_update_and_the_row_reappears()
    {
        var id = await AddChequeAsync("chq-undo");

        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChequeDb>();
            db.Cheques.Remove(await db.Cheques.SingleAsync(c => c.Id == id));
            await db.SaveChangesAsync();
        }

        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChequeDb>();
            var deleted = await db.Cheques.IgnoreQueryFilters().SingleAsync(c => c.Id == id);
            deleted.IsDeleted = false;
            deleted.DeletedAt = null;
            deleted.DeletedBy = null;
            await db.SaveChangesAsync();
        }

        using var verify = NewScope();
        Assert.Equal(1, await verify.ServiceProvider.GetRequiredService<ChequeDb>()
            .Cheques.CountAsync(c => c.Id == id));                                         // visible again
    }
}
