using MassTransit;

namespace CorPay.Api.Payments;

/// <summary>Broker-bound (outboxed) — the ledger feed's input (GP0401).</summary>
public record PaymentExecuted(long InstructionId, string Reference, decimal Amount, string Currency) : IIntegrationEvent;

/// <summary>
/// Feeds the ledger from the outboxed event — one row per executed payment, written by
/// the consumer so the feed survives an API crash after execution (the outbox guarantees
/// the event; the inbox guarantees once).
/// </summary>
public class PaymentExecutedConsumer(Orders.OrdersDbContext db) : IConsumer<PaymentExecuted>
{
    public async Task Consume(ConsumeContext<PaymentExecuted> context)
    {
        db.Set<LedgerFeedEntry>().Add(new LedgerFeedEntry
        {
            InstructionId = context.Message.InstructionId,
            Reference = context.Message.Reference,
            Amount = context.Message.Amount,
            Currency = context.Message.Currency,
            FedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

/// <summary>The core-banking ledger's inbound feed row (EOD reconciles against these).</summary>
public class LedgerFeedEntry
{
    public long Id { get; set; }
    public long InstructionId { get; set; }
    public string Reference { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public DateTimeOffset FedAt { get; set; }
}
