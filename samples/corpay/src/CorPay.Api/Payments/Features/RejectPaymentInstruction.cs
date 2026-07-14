using Microsoft.EntityFrameworkCore;

namespace CorPay.Api.Payments.Features;

[HttpEndpoint("POST", "/api/v1/payment-instructions/{id}/reject")]
public record RejectPaymentInstructionCommand(long Id, string Reason) : ICommand<Result<long>>;

/// <summary>Rejection is a decision, so it needs a decider and a WHY — both become evidence.</summary>
public class RejectPaymentInstructionHandler(Orders.OrdersDbContext db, IUserContext user)
    : ICommandHandler<RejectPaymentInstructionCommand, Result<long>>
{
    public async ValueTask<Result<long>> Handle(RejectPaymentInstructionCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.ValidationFailure<long>(new ValidationError(nameof(request.Reason), "a rejection needs a reason"));
        }

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
            return Result.Failure<long>(new Error("Payment.DeciderRequired", "an authenticated decider is required", ErrorType.Forbidden));
        }

        instruction.Status = PaymentStatus.Rejected;
        instruction.RejectionReason = request.Reason;
        await db.SaveChangesAsync(cancellationToken);   // the change rows carry old -> new + the decider stamp

        return Result.Success(instruction.Id);
    }
}
