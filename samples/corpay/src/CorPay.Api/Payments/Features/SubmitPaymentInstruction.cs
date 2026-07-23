using System.Text.RegularExpressions;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace CorPay.Api.Payments.Features;

[HttpEndpoint("POST", "/api/v1/payment-instructions")]
public record SubmitPaymentInstructionCommand(
    string Reference,
    string DebtorIban,
    string CreditorIban,
    decimal Amount,
    string Currency) : ICommand<Result<long>>;

/// <summary>
/// Slice 1 of the finance card: validate → persist Submitted → execute against the
/// core-banking port → Executed + the outboxed <see cref="PaymentExecuted"/> event.
/// Client retries ride the HTTP Idempotency-Key middleware (same key → same instruction,
/// ONE execution); the per-tenant duplicate-reference check is the belt under that suspender.
/// </summary>
public partial class SubmitPaymentInstructionHandler(Orders.OrdersDbContext db, ICoreBankingClient coreBanking, IPublishEndpoint publisher)
    : ICommandHandler<SubmitPaymentInstructionCommand, Result<long>>
{
    private static readonly string[] Currencies = ["TRY", "EUR", "USD"];

    public async ValueTask<Result<long>> Handle(SubmitPaymentInstructionCommand request, CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Result.ValidationFailure<long>([.. errors]);
        }

        var duplicate = await db.Set<PaymentInstruction>()
            .AnyAsync(i => i.Reference == request.Reference, cancellationToken);   // tenant filter is live
        if (duplicate)
        {
            return Result.ValidationFailure<long>(
                new ValidationError(nameof(request.Reference), "an instruction with this reference already exists for your tenant"));
        }

        var instruction = new PaymentInstruction
        {
            Reference = request.Reference,
            DebtorIban = request.DebtorIban,
            CreditorIban = request.CreditorIban,
            Amount = request.Amount,
            Currency = request.Currency,
        };
        db.Set<PaymentInstruction>().Add(instruction);

        // Four-eyes control: at/above the threshold NO money moves on submit — a second
        // person approves (or rejects) through the dedicated verbs.
        if (request.Amount >= PaymentPolicy.FourEyesThreshold)
        {
            instruction.Status = PaymentStatus.PendingApproval;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(instruction.Id);
        }

        // ONE transaction end to end: the Submitted row, the Executed flip and the outbox
        // row commit together — a crash anywhere leaves either no instruction or an
        // executed one WITH its event; never an executed payment the ledger never hears of.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);           // Submitted row (id + audit rows)

        instruction.ExecutionReceipt = await coreBanking.ExecuteAsync(instruction, cancellationToken);
        instruction.Status = PaymentStatus.Executed;
        await publisher.Publish(new PaymentExecuted(instruction.Id, instruction.Reference, instruction.Amount, instruction.Currency), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);           // Executed flip + outbox row
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(instruction.Id);
    }

    internal static List<ValidationError> Validate(SubmitPaymentInstructionCommand request)
    {
        var errors = new List<ValidationError>();
        if (string.IsNullOrWhiteSpace(request.Reference))
        {
            errors.Add(new ValidationError(nameof(request.Reference), "reference is required"));
        }

        if (request.Amount <= 0)
        {
            errors.Add(new ValidationError(nameof(request.Amount), "amount must be positive"));
        }
        else if (decimal.Round(request.Amount, 2) != request.Amount)
        {
            // Breaker finding (BREAKER-VERDICT.md): every whitelisted currency settles in
            // 2 minor units — a sub-cent amount is un-settleable and must die at the door.
            errors.Add(new ValidationError(nameof(request.Amount), "amount must not have more than 2 decimal places"));
        }

        if (!Currencies.Contains(request.Currency))
        {
            errors.Add(new ValidationError(nameof(request.Currency), "currency must be one of TRY, EUR, USD"));
        }

        if (!Iban().IsMatch(request.DebtorIban))
        {
            errors.Add(new ValidationError(nameof(request.DebtorIban), "debtor IBAN shape is invalid"));
        }

        if (!Iban().IsMatch(request.CreditorIban))
        {
            errors.Add(new ValidationError(nameof(request.CreditorIban), "creditor IBAN shape is invalid"));
        }

        return errors;
    }

    // Shape check only (country + 2 check digits + up to 30 alphanumerics); the bank
    // validates for real — rejecting obvious garbage early is the API's job.
    [GeneratedRegex("^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$")]
    private static partial Regex Iban();
}
