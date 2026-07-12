namespace Goldpath;

/// <summary>
/// Marks an event that crosses the service boundary via the message broker
/// (published through the MassTransit transactional outbox). In-process domain events
/// are Mediant notifications and must NOT carry this marker — the boundary is defined
/// by the Messaging RFC.
/// </summary>
public interface IIntegrationEvent;
