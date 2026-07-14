using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace CorPay.Api.Payments.Features;

[HttpEndpoint("POST", "/api/v1/payment-instructions/{id}/approve")]
public record ApprovePaymentInstructionCommand(long Id) : ICommand<Result<long>>;

/// <summary>
/// The second pair of eyes: a DIFFERENT authenticated person releases a pending
/// instruction — then the same one-transaction execute path as submit (bank receipt,
/// Executed flip, outboxed event). The approver lands on the row AND in the audit rows.
/// </summary>
public class ApprovePaymentInstructionHandler(Orders.OrdersDbContext db, ICoreBankingClient coreBanking, IPublishEndpoint publisher, IUserContext user)
    : ICommandHandler<ApprovePaymentInstructionCommand, Result<long>>
{
    public async ValueTask<Result<long>> Handle(ApprovePaymentInstructionCommand request, CancellationToken cancellationToken)
    {
        var instruction = await db.Set<PaymentInstruction>()
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken);   // tenant filter is live
        if (instruction is null)
        {
            return Result.Failure<long>(new Error("Payment.NotFound", "no such instruction for your tenant", ErrorType.NotFound));
        }

        if (instruction.Status != PaymentStatus.PendingApproval)
        {
            return Result.Failure<long>(new Error("Payment.NotPending", $"instruction is {instruction.Status}, not PendingApproval", ErrorType.Conflict));
        }

        if (string.IsNullOrEmpty(user.UserId))
        {
            return Result.Failure<long>(new Error("Payment.ApproverRequired", "an authenticated approver is required", ErrorType.Forbidden));
        }

        if (user.UserId == instruction.CreatedBy)
        {
            return Result.Failure<long>(new Error("Payment.FourEyes", "the submitter cannot approve their own instruction", ErrorType.Forbidden));
        }

        instruction.ApprovedBy = user.UserId;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        instruction.ExecutionReceipt = await coreBanking.ExecuteAsync(instruction, cancellationToken);
        instruction.Status = PaymentStatus.Executed;
        await publisher.Publish(new PaymentExecuted(instruction.Id, instruction.Reference, instruction.Amount, instruction.Currency), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(instruction.Id);
    }
}
