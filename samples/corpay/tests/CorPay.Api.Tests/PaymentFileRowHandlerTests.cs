using CorPay.Api.Orders;
using CorPay.Api.Payments;
using CorPay.Api.Payments.Import;
using Goldpath;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CorPay.Api.Tests;

/// <summary>
/// The batch row handler's money rule: one reference pays ONCE — a replayed row that
/// already executed completes quietly without touching core banking again.
/// </summary>
public class PaymentFileRowHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly OrdersDbContext _db;

    public PaymentFileRowHandlerTests()
    {
        _connection.Open();
        // The real save pipeline, unit-sized: the tenant stamp rides the same contributor
        // interceptor production wires through AddGoldpathData.
        _db = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>()
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

    private sealed class CountingBank : ICoreBankingClient
    {
        public int Executions;

        public Task<string> ExecuteAsync(PaymentInstruction instruction, CancellationToken cancellationToken)
        {
            Executions++;
            return Task.FromResult($"RCPT-{instruction.Reference}");
        }
    }

    [Fact]
    public async Task A_replayed_row_never_pays_twice()
    {
        using var tenant = GoldpathTenant.Use("acme");
        var bank = new CountingBank();
        var publisher = new RecordingPublisher();
        var handler = new PaymentFileRowHandler(_db, bank, publisher);
        var row = new PaymentFileRow
        {
            Reference = "PAY-9",
            DebtorIban = "TR330006100519786457841326",
            CreditorIban = "DE89370400440532013000",
            Amount = 100m,
            Currency = "EUR",
        };
        // The handler never touches the row context (ctor is engine-internal) — null is honest here.
        await handler.ExecuteAsync(row, null!, CancellationToken.None);
        await _db.SaveChangesAsync();   // the chunk's batched save, played by the test

        await handler.ExecuteAsync(row, null!, CancellationToken.None);   // the replay

        Assert.Equal(1, bank.Executions);
        Assert.Single(publisher.Published);
        var instruction = Assert.Single(_db.Set<PaymentInstruction>().ToList());
        Assert.Equal(PaymentStatus.Executed, instruction.Status);
        Assert.Equal("RCPT-PAY-9", instruction.ExecutionReceipt);
    }
}

/// <summary>Minimal IPublishEndpoint: records messages, ignores observers.</summary>
internal sealed class RecordingPublisher : IPublishEndpoint
{
    public List<object> Published { get; } = [];

    public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        => Publish(message, cancellationToken);

    public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        => Publish(message, cancellationToken);

    public Task Publish(object message, CancellationToken cancellationToken = default)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        => Publish(message, cancellationToken);

    public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
        => Publish(message, cancellationToken);

    public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        => Publish(message, cancellationToken);

    public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class
    {
        Published.Add(values);
        return Task.CompletedTask;
    }

    public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        => Publish<T>(values, cancellationToken);

    public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        => Publish<T>(values, cancellationToken);

    public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotSupportedException();
}

/// <summary>The DB-enforced money rule: one reference per tenant, even under a race.</summary>
public class ReferenceUniquenessTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection = new("DataSource=:memory:");
    private readonly CorPay.Api.Orders.OrdersDbContext _db;

    public ReferenceUniquenessTests()
    {
        _connection.Open();
        _db = new CorPay.Api.Orders.OrdersDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<CorPay.Api.Orders.OrdersDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(new Goldpath.GoldpathSaveChangesInterceptor(
                    [new Goldpath.TenantStampContributor()], new Goldpath.GoldpathSaveContext(TimeProvider.System, null)))
                .Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static CorPay.Api.Payments.PaymentInstruction Instruction() => new()
    {
        Reference = "PAY-RACE",
        DebtorIban = "TR330006100519786457841326",
        CreditorIban = "DE89370400440532013000",
        Amount = 10m,
        Currency = "TRY",
    };

    [Fact]
    public void The_same_reference_cannot_land_twice_for_one_tenant()
    {
        using (Goldpath.GoldpathTenant.Use("acme"))
        {
            _db.Add(Instruction());
            _db.SaveChanges();
            _db.Add(Instruction());
            Assert.Throws<Microsoft.EntityFrameworkCore.DbUpdateException>(() => _db.SaveChanges());
            _db.ChangeTracker.Clear();
        }

        using (Goldpath.GoldpathTenant.Use("rival"))
        {
            _db.Add(Instruction());   // another tenant's book, another row — fine
            _db.SaveChanges();
        }
    }
}
