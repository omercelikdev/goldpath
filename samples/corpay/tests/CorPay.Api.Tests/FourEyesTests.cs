using CorPay.Api.Orders;
using CorPay.Api.Payments;
using CorPay.Api.Payments.Features;
using Goldpath;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CorPay.Api.Tests;

/// <summary>
/// The four-eyes contract: big tickets park as PendingApproval, a DIFFERENT authenticated
/// person releases them (or rejects with a reason), and only the release moves money.
/// </summary>
public class FourEyesTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly OrdersDbContext _db;
    private readonly CountingBank _bank = new();
    private readonly RecordingPublisher _publisher = new();

    public FourEyesTests()
    {
        _connection.Open();
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
            return Task.FromResult("RCPT");
        }
    }

    private sealed class FakeUser(string? id) : IUserContext
    {
        public string? UserId => id;
    }

    private static SubmitPaymentInstructionCommand Big(string reference = "BIG-1") =>
        new(reference, "TR330006100519786457841326", "DE89370400440532013000", PaymentPolicy.FourEyesThreshold, "TRY");

    private async Task<long> SubmitBigAsync()
    {
        var submit = new SubmitPaymentInstructionHandler(_db, _bank, _publisher);
        var result = await submit.Handle(Big(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public async Task At_the_threshold_no_money_moves_on_submit()
    {
        using var tenant = GoldpathTenant.Use("acme");
        var id = await SubmitBigAsync();

        var instruction = await _db.Set<PaymentInstruction>().SingleAsync(i => i.Id == id);
        Assert.Equal(PaymentStatus.PendingApproval, instruction.Status);
        Assert.Equal(0, _bank.Executions);
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task A_different_approver_releases_and_the_money_moves_once()
    {
        using var tenant = GoldpathTenant.Use("acme");
        var id = await SubmitBigAsync();
        var instruction = await _db.Set<PaymentInstruction>().SingleAsync(i => i.Id == id);
        instruction.CreatedBy = "treasurer";
        await _db.SaveChangesAsync();

        var approve = new ApprovePaymentInstructionHandler(_db, _bank, _publisher, new FakeUser("ops-chief"));
        var result = await approve.Handle(new(id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var released = await _db.Set<PaymentInstruction>().SingleAsync(i => i.Id == id);
        Assert.Equal(PaymentStatus.Executed, released.Status);
        Assert.Equal("ops-chief", released.ApprovedBy);
        Assert.Equal(1, _bank.Executions);
        Assert.Single(_publisher.Published);
    }

    [Fact]
    public async Task The_submitter_cannot_approve_their_own_instruction()
    {
        using var tenant = GoldpathTenant.Use("acme");
        var id = await SubmitBigAsync();
        var instruction = await _db.Set<PaymentInstruction>().SingleAsync(i => i.Id == id);
        instruction.CreatedBy = "treasurer";
        await _db.SaveChangesAsync();

        var approve = new ApprovePaymentInstructionHandler(_db, _bank, _publisher, new FakeUser("treasurer"));
        var result = await approve.Handle(new(id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, _bank.Executions);
        Assert.Equal(PaymentStatus.PendingApproval, (await _db.Set<PaymentInstruction>().SingleAsync(i => i.Id == id)).Status);
    }

    [Fact]
    public async Task Rejection_requires_a_reason_and_never_pays()
    {
        using var tenant = GoldpathTenant.Use("acme");
        var id = await SubmitBigAsync();

        var reject = new RejectPaymentInstructionHandler(_db, new FakeUser("ops-chief"));
        Assert.False((await reject.Handle(new(id, " "), CancellationToken.None)).IsSuccess);

        var result = await reject.Handle(new(id, "beneficiary flagged by compliance"), CancellationToken.None);
        Assert.True(result.IsSuccess);
        var rejected = await _db.Set<PaymentInstruction>().SingleAsync(i => i.Id == id);
        Assert.Equal(PaymentStatus.Rejected, rejected.Status);
        Assert.Equal("beneficiary flagged by compliance", rejected.RejectionReason);
        Assert.Equal(0, _bank.Executions);
    }

    [Fact]
    public async Task An_executed_instruction_cannot_be_approved_again()
    {
        using var tenant = GoldpathTenant.Use("acme");
        var submit = new SubmitPaymentInstructionHandler(_db, _bank, _publisher);
        var small = await submit.Handle(Big("SMALL-1") with { Amount = 10m }, CancellationToken.None);
        Assert.True(small.IsSuccess);

        var approve = new ApprovePaymentInstructionHandler(_db, _bank, _publisher, new FakeUser("ops-chief"));
        var result = await approve.Handle(new(small.Value), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, _bank.Executions);   // only the original submit paid
    }
}
