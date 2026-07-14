using CorPay.Api.Orders;
using CorPay.Api.Payments;
using CorPay.Api.Payments.Features;
using Goldpath;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CorPay.Api.Tests;

/// <summary>The day report shows MY tenant's day — counts, executed total, ledger rows.</summary>
public class DayReportTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly OrdersDbContext _db;

    public DayReportTests()
    {
        _connection.Open();
        _db = new SqliteOrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new GoldpathSaveChangesInterceptor(
                [new TenantStampContributor()], new GoldpathSaveContext(TimeProvider.System, null)))
            .Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private void Seed(string tenant, PaymentStatus status, decimal amount)
    {
        using var scope = GoldpathTenant.Use(tenant);
        _db.Set<PaymentInstruction>().Add(new PaymentInstruction
        {
            Reference = Guid.NewGuid().ToString("N")[..12],
            DebtorIban = "TR330006100519786457841326",
            CreditorIban = "DE89370400440532013000",
            Amount = amount,
            Currency = "TRY",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task The_report_counts_only_my_tenant_and_sums_executed()
    {
        Seed("acme", PaymentStatus.Executed, 100m);
        Seed("acme", PaymentStatus.Executed, 250m);
        Seed("acme", PaymentStatus.PendingApproval, 999_999m);
        Seed("acme", PaymentStatus.Rejected, 5m);
        Seed("rival", PaymentStatus.Executed, 77m);   // the fence under test

        using var tenant = GoldpathTenant.Use("acme");
        var handler = new GetDayReportHandler(_db, TimeProvider.System);
        var report = await handler.Handle(new GetDayReportQuery(), CancellationToken.None);

        Assert.True(report.IsSuccess);
        Assert.Equal(2, report.Value.Executed);
        Assert.Equal(350m, report.Value.ExecutedTotal);
        Assert.Equal(1, report.Value.PendingApproval);
        Assert.Equal(1, report.Value.Rejected);
    }
}
