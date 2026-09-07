namespace GoldpathTemplate.Application.Orders;

/// <summary>
/// Handles the outboxed event and confirms the order — the walking skeleton's proof that
/// the full loop (HTTP → outbox → broker → handler → data) is alive. Through the seam
/// (ADR-0013): no bus type here, the queue is named after the handler (order-placed).
/// </summary>
public class OrderPlacedHandler(IOrdersDbContext db) : IIntegrationEventHandler<OrderPlaced>
{
    /// <inheritdoc />
    public async Task HandleAsync(OrderPlaced integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.FindAsync([integrationEvent.OrderId], cancellationToken);
        if (order is { Status: OrderStatus.Pending })
        {
            order.Status = OrderStatus.Confirmed;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
