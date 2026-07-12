using MassTransit;

namespace GoldpathTemplate.Api.Orders;

/// <summary>Broker-bound (outboxed) — hence the IIntegrationEvent marker (GP0401).</summary>
public record OrderPlaced(long OrderId) : IIntegrationEvent;

/// <summary>
/// Consumes the outboxed event and confirms the order — the walking skeleton's proof that
/// the full loop (HTTP → outbox → broker → consumer → data) is alive.
/// </summary>
public class OrderPlacedConsumer(OrdersDbContext db) : IConsumer<OrderPlaced>
{
    public async Task Consume(ConsumeContext<OrderPlaced> context)
    {
        var order = await db.Orders.FindAsync([context.Message.OrderId], context.CancellationToken);
        if (order is { Status: OrderStatus.Pending })
        {
            order.Status = OrderStatus.Confirmed;
            await db.SaveChangesAsync(context.CancellationToken);
        }
    }
}
