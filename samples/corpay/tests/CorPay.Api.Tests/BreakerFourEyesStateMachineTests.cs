using CorPay.Api.Orders;
using CorPay.Api.Payments;
using CorPay.Api.Payments.Features;
using Goldpath;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CorPay.Api.Tests;

/// <summary>
/// BREAKER — adversarial state-machine, four-eyes and tenant-fence abuse of the
/// payment-instructions surface. These are hostile call sequences a caller can legally
/// issue: double-approve replay, reject-after-execute, approve-after-reject, a rival
/// tenant approving/rejecting my id, and unknown ids. The money invariant under attack:
/// IBankGateway executes AT MOST ONCE per instruction, and never for a foreign caller.
/// Public seams discovered only through the compiler (breaker context rule): no src/ read.
/// </summary>
public class BreakerFourEyesStateMachineTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly OrdersDbContext _db;
    private readonly CountingBank _bank = new();
    private readonly BreakerPublisher _publisher = new();

    public BreakerFourEyesStateMachineTests()
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
        GC.SuppressFinalize(this);
    }

    private sealed class CountingBank : ICoreBankingClient
    {
        public int Executions;

        public Task<string> ExecuteAsync(PaymentInstruction instruction, CancellationToken cancellationToken)
        {
            Executions++;
            return Task.FromResult("RCPT");
        }
    }

    private sealed class FakeUser(string? id) : IUserContext
    {
        public string? UserId => id;
    }

    private static SubmitPaymentInstructionCommand Big(string reference = "BIG-1") =>
        new(reference, "TR330006100519786457841326", "DE89370400440532013000", PaymentPolicy.FourEyesThreshold, "TRY");

    private async Task<long> SubmitBigAsync(string reference = "BIG-1", string createdBy = "treasurer")
    {
        var submit = new SubmitPaymentInstructionHandler(_db, _bank, _publisher);
        var result = await submit.Handle(Big(reference), CancellationToken.None);
        Assert.True(result.IsSuccess);
        // Stamp a distinct submitter so the four-eyes rule cannot mask a state-machine bug.
        var instruction = await _db.Set<PaymentInstruction>().SingleAsync(i => i.Id == result.Value);
        instruction.CreatedBy = createdBy;
        await _db.SaveChangesAsync();
        return result.Value;
    }

    private async Task<PaymentStatus> StatusOfAsync(long id) =>
        (await _db.Set<PaymentInstruction>().SingleAsync(i => i.Id == id)).Status;

    // ---- Target 1: state machine / double-approve replay (G6) --------------------------

    [Fact]
    public async Task Breaker_double_approve_never_pays_twice()
    {
        using var tenant = GoldpathTenant.Use("acme");
        var id = await SubmitBigAsync();

        var first = new ApprovePaymentInstructionHandler(_db, _bank, _publisher, new FakeUser("ops-chief"));
        Assert.True((await first.Handle(new(id), CancellationToken.None)).IsSuccess);
        Assert.Equal(PaymentStatus.Executed, await StatusOfAsync(id));
        Assert.Equal(1, _bank.Executions);

        // The replay: a DIFFERENT valid approver re-approves the already-Executed ticket.
        var second = new ApprovePaymentInstructionHandler(_db, _bank, _publisher, new FakeUser("auditor"));
        var replay = await second.Handle(new(id), CancellationToken.None);

        Assert.False(replay.IsSuccess);
        Assert.Equal(1, _bank.Executions);              // money moved exactly once
        Assert.Equal(PaymentStatus.Executed, await StatusOfAsync(id));
    }

    [Fact]
    public async Task Breaker_reject_after_execute_is_refused_and_state_holds()
    {
        using var tenant = GoldpathTenant.Use("acme");
        var id = await SubmitBigAsync();

        var approve = new ApprovePaymentInstructionHandler(_db, _bank, _publisher, new FakeUser("ops-chief"));
        Assert.True((await approve.Handle(new(id), CancellationToken.None)).IsSuccess);
        Assert.Equal(1, _bank.Executions);

        var reject = new RejectPaymentInstructionHandler(_db, new FakeUser("auditor"));
        var result = await reject.Handle(new(id, "trying to unwind an executed payment"), CancellationToken.None);

        Assert.False(result.IsSuccess);                 // an executed payment is not rejectable
        Assert.Equal(PaymentStatus.Executed, await StatusOfAsync(id));
        Assert.Equal(1, _bank.Executions);
    }

    [Fact]
    public async Task Breaker_approve_after_reject_is_refused_and_never_pays()
    {
        using var tenant = GoldpathTenant.Use("acme");
        var id = await SubmitBigAsync();

        var reject = new RejectPaymentInstructionHandler(_db, new FakeUser("ops-chief"));
        Assert.True((await reject.Handle(new(id, "beneficiary flagged"), CancellationToken.None)).IsSuccess);
        Assert.Equal(PaymentStatus.Rejected, await StatusOfAsync(id));

        // Rejected must be terminal: a later approve cannot resurrect and pay it.
        var approve = new ApprovePaymentInstructionHandler(_db, _bank, _publisher, new FakeUser("auditor"));
        var result = await approve.Handle(new(id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(PaymentStatus.Rejected, await StatusOfAsync(id));
        Assert.Equal(0, _bank.Executions);
    }

    // ---- Target 2: tenant fence under hostility (manifest multiTenancy: true) -----------

    [Fact]
    public async Task Breaker_foreign_tenant_cannot_approve_and_no_money_moves()
    {
        long id;
        using (GoldpathTenant.Use("acme"))
        {
            id = await SubmitBigAsync();
        }

        // Rival tenant tries to release acme's parked instruction by its raw id.
        using (GoldpathTenant.Use("rival"))
        {
            var approve = new ApprovePaymentInstructionHandler(_db, _bank, _publisher, new FakeUser("rival-boss"));
            var result = await approve.Handle(new(id), CancellationToken.None);
            Assert.False(result.IsSuccess);
        }

        Assert.Equal(0, _bank.Executions);
        using (GoldpathTenant.Use("acme"))
        {
            Assert.Equal(PaymentStatus.PendingApproval, await StatusOfAsync(id));
        }
    }

    [Fact]
    public async Task Breaker_foreign_tenant_cannot_reject_my_instruction()
    {
        long id;
        using (GoldpathTenant.Use("acme"))
        {
            id = await SubmitBigAsync();
        }

        using (GoldpathTenant.Use("rival"))
        {
            var reject = new RejectPaymentInstructionHandler(_db, new FakeUser("rival-boss"));
            var result = await reject.Handle(new(id, "sabotage"), CancellationToken.None);
            Assert.False(result.IsSuccess);
        }

        using (GoldpathTenant.Use("acme"))
        {
            Assert.Equal(PaymentStatus.PendingApproval, await StatusOfAsync(id));
        }
    }

    // ---- Target 3: unknown / foreign id on approve & reject (G4) ------------------------

    [Fact]
    public async Task Breaker_unknown_id_approve_fails_without_throwing()
    {
        using var tenant = GoldpathTenant.Use("acme");
        var approve = new ApprovePaymentInstructionHandler(_db, _bank, _publisher, new FakeUser("ops-chief"));

        var result = await approve.Handle(new(999_999L), CancellationToken.None);

        Assert.False(result.IsSuccess);   // an id that never existed must be a clean failure, not a throw
        Assert.Equal(0, _bank.Executions);
    }

    [Fact]
    public async Task Breaker_unknown_id_reject_fails_without_throwing()
    {
        using var tenant = GoldpathTenant.Use("acme");
        var reject = new RejectPaymentInstructionHandler(_db, new FakeUser("ops-chief"));

        var result = await reject.Handle(new(999_999L, "no such ticket"), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}

/// <summary>Minimal IPublishEndpoint local to the breaker suite: records, ignores observers.</summary>
internal sealed class BreakerPublisher : IPublishEndpoint
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
