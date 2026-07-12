using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

public sealed class TenantIsolationTests : IDisposable
{
    private sealed class Loan : IMultiTenant
    {
        public long Id { get; set; }
        public string? Number { get; set; }
        public TenantId TenantId { get; set; }
    }

    private sealed class Cheque : IMultiTenant, ISoftDeletable
    {
        public long Id { get; set; }
        public TenantId TenantId { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }

    private sealed class BankDb(DbContextOptions<BankDb> options) : DbContext(options)
    {
        public DbSet<Loan> Loans => Set<Loan>();
        public DbSet<Cheque> Cheques => Set<Cheque>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyGoldpathSoftDelete();
            modelBuilder.ApplyGoldpathMultiTenancy(this);   // order-independent: filters AND-combine
        }
    }

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IHost _host;

    public TenantIsolationTests()
    {
        _connection.Open();
        var builder = Host.CreateApplicationBuilder();
        builder.AddGoldpathMultiTenancy();
        builder.AddGoldpathSoftDelete();
        builder.AddGoldpathData<HostApplicationBuilder, BankDb>(o => o.UseSqlite(_connection));
        _host = builder.Build();

        using var scope = _host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<BankDb>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _host.Dispose();
        _connection.Dispose();
    }

    private BankDb Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<BankDb>();

    private async Task SeedAsync(string tenant, string number)
    {
        using var scope = _host.Services.CreateScope();
        using (GoldpathTenant.Use(tenant))
        {
            var db = Db(scope);
            db.Loans.Add(new Loan { Number = number });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Added_rows_are_stamped_and_queries_see_only_the_ambient_tenant()
    {
        await SeedAsync("acme", "L-1");
        await SeedAsync("globex", "L-2");

        using var scope = _host.Services.CreateScope();
        using (GoldpathTenant.Use("acme"))
        {
            var loans = await Db(scope).Loans.ToListAsync();
            var loan = Assert.Single(loans);
            Assert.Equal("L-1", loan.Number);
            Assert.Equal("acme", loan.TenantId.Value);   // stamped by the contributor
        }
    }

    [Fact]
    public async Task No_ambient_tenant_means_no_rows_fail_closed()
    {
        await SeedAsync("acme", "L-1");

        using var scope = _host.Services.CreateScope();
        Assert.Equal(0, await Db(scope).Loans.CountAsync());
    }

    [Fact]
    public async Task Bypass_widens_reads_and_only_inside_the_scope()
    {
        await SeedAsync("acme", "L-1");
        await SeedAsync("globex", "L-2");

        using var scope = _host.Services.CreateScope();
        using (GoldpathTenant.Bypass())
        {
            Assert.Equal(2, await Db(scope).Loans.CountAsync());
        }

        Assert.Equal(0, await Db(scope).Loans.CountAsync());   // scope ended → fail-closed again
    }

    [Fact]
    public async Task Writing_another_tenants_row_trips_the_guard_and_aborts()
    {
        await SeedAsync("acme", "L-1");

        using var scope = _host.Services.CreateScope();
        var db = Db(scope);
        Loan victim;
        using (GoldpathTenant.Bypass())
        {
            victim = await db.Loans.SingleAsync();
        }

        using (GoldpathTenant.Use("globex"))
        {
            victim.Number = "L-1-hijacked";
            await Assert.ThrowsAsync<GoldpathCrossTenantWriteException>(() => db.SaveChangesAsync());
        }

        // The write never happened.
        using var verify = _host.Services.CreateScope();
        using (GoldpathTenant.Use("acme"))
        {
            Assert.Equal("L-1", (await Db(verify).Loans.SingleAsync()).Number);
        }
    }

    [Fact]
    public async Task Guard_stays_on_inside_bypass_writes_need_an_explicit_tenant()
    {
        await SeedAsync("acme", "L-1");

        using var scope = _host.Services.CreateScope();
        var db = Db(scope);
        using (GoldpathTenant.Bypass())
        {
            var loan = await db.Loans.SingleAsync();
            loan.Number = "L-1-admin-edit";
            await Assert.ThrowsAsync<GoldpathCrossTenantWriteException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Adding_with_an_explicit_foreign_tenant_trips_the_guard()
    {
        using var scope = _host.Services.CreateScope();
        var db = Db(scope);
        using (GoldpathTenant.Use("acme"))
        {
            db.Loans.Add(new Loan { Number = "L-x", TenantId = TenantId.Create("globex") });
            await Assert.ThrowsAsync<GoldpathCrossTenantWriteException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Adding_without_any_ambient_tenant_trips_the_guard()
    {
        using var scope = _host.Services.CreateScope();
        var db = Db(scope);
        db.Loans.Add(new Loan { Number = "L-orphan" });

        await Assert.ThrowsAsync<GoldpathCrossTenantWriteException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SoftDelete_and_tenant_filters_combine_on_one_entity()
    {
        using (var scope = _host.Services.CreateScope())
        using (GoldpathTenant.Use("acme"))
        {
            var db = Db(scope);
            db.Cheques.Add(new Cheque());
            db.Cheques.Add(new Cheque());
            await db.SaveChangesAsync();
            db.Cheques.Remove(await db.Cheques.FirstAsync());
            await db.SaveChangesAsync();                      // soft-deleted, still acme-owned
        }

        using (var scope = _host.Services.CreateScope())
        using (GoldpathTenant.Use("globex"))
        {
            var db = Db(scope);
            db.Cheques.Add(new Cheque());
            await db.SaveChangesAsync();
        }

        using var verify = _host.Services.CreateScope();
        using (GoldpathTenant.Use("acme"))
        {
            // acme sees: its two rows minus the soft-deleted one; never globex's.
            Assert.Equal(1, await Db(verify).Cheques.CountAsync());
        }

        using (GoldpathTenant.Bypass())
        {
            // Bypass lifts the TENANT filter; the soft-delete filter still hides the deleted row.
            Assert.Equal(2, await Db(verify).Cheques.CountAsync());
        }
    }
}
