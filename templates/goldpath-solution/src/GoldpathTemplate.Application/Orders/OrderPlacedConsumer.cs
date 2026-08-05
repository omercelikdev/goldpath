using MassTransit;

namespace GoldpathTemplate.Application.Orders;

/// <summary>
/// Consumes the outboxed event and confirms the order — the walking skeleton's proof that
/// the full loop (HTTP → outbox → broker → consumer → data) is alive.
/// </summary>
public class OrderPlacedConsumer(IOrdersDbContext db) : IConsumer<OrderPlaced>
{
    /// <inheritdoc />
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
