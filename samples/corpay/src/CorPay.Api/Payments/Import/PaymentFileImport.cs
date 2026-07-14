using CorPay.Api.Payments.Features;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace CorPay.Api.Payments.Import;

/// <summary>
/// One row of the corporate batch-payment file (slice 2). Columns map by HEADER NAME —
/// a reordered export cannot shift money between fields. Validation reuses the SAME rule
/// table as the single-submit endpoint: one contract, two intakes.
/// </summary>
public sealed class PaymentFileRow
{
    /// <summary>End-to-end reference — also the in-file duplicate key.</summary>
    public string Reference { get; set; } = "";

    public string DebtorIban { get; set; } = "";

    public string CreditorIban { get; set; } = "";

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "";
}

/// <summary>
/// Executes one APPROVED, VALID row: the instruction persists, core banking pays, the
/// PaymentExecuted event stages onto the SAME scoped context — everything commits with
/// the chunk's batched save (GP1502: no SaveChanges here). Throwing sends the row to the
/// repair queue; a reference that already executed is replay evidence, not a re-pay.
/// </summary>
public sealed class PaymentFileRowHandler(Orders.OrdersDbContext db, ICoreBankingClient coreBanking, IPublishEndpoint publisher)
    : IGoldpathBulkRowHandler<PaymentFileRow>
{
    /// <inheritdoc />
    public async Task ExecuteAsync(PaymentFileRow row, GoldpathBulkRowContext context, CancellationToken cancellationToken)
    {
        var exists = await db.Set<PaymentInstruction>()
            .AnyAsync(i => i.Reference == row.Reference, cancellationToken);   // tenant filter is live
        if (exists)
        {
            return;   // idempotent replay: the money moved once; the row completes quietly
        }

        var instruction = new PaymentInstruction
        {
            Reference = row.Reference,
            DebtorIban = row.DebtorIban,
            CreditorIban = row.CreditorIban,
            Amount = row.Amount,
            Currency = row.Currency,
        };

        // The SAME four-eyes control as single submit: file rows at/above the threshold
        // park as PendingApproval — the batch gate approved the FILE, not each big ticket.
        if (row.Amount >= PaymentPolicy.FourEyesThreshold)
        {
            instruction.Status = PaymentStatus.PendingApproval;
            db.Set<PaymentInstruction>().Add(instruction);
            return;
        }

        instruction.ExecutionReceipt = await coreBanking.ExecuteAsync(instruction, cancellationToken);
        instruction.Status = PaymentStatus.Executed;
        db.Set<PaymentInstruction>().Add(instruction);
        await publisher.Publish(
            new PaymentExecuted(instruction.Id, instruction.Reference, instruction.Amount, instruction.Currency), cancellationToken);
        // the chunk's batched SaveChanges persists the instruction, its audit rows AND the outbox row together
    }
}
