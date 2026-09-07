namespace CorPay.Api.Orders;

/// <summary>Broker-bound (outboxed) — hence the IIntegrationEvent marker (GP0401).</summary>
public record OrderPlaced(long OrderId) : IIntegrationEvent;

/// <summary>
/// Handles the outboxed event and confirms the order — the walking skeleton's proof that
/// the full loop (HTTP → outbox → broker → handler → data) is alive. Through the seam
/// (ADR-0013): no bus type here, and the queue is named as the consumer's was (order-placed),
/// so this is a code change and not a wire change.
/// </summary>
public class OrderPlacedHandler(OrdersDbContext db) : IIntegrationEventHandler<OrderPlaced>
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
