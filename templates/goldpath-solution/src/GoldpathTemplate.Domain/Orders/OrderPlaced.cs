namespace GoldpathTemplate.Domain.Orders;

/// <summary>Broker-bound (outboxed) — hence the IIntegrationEvent marker (GP0401).</summary>
public record OrderPlaced(long OrderId) : IIntegrationEvent;
