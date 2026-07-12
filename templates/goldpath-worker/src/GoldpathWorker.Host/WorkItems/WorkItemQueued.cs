namespace GoldpathWorker.Host.WorkItems;

/// <summary>
/// The broker-bound contract this worker drains (implements <c>IIntegrationEvent</c> —
/// GP0401). Rename/replace it with the real upstream event; the queue name in the manifest
/// follows the consumer's kebab-case name.
/// </summary>
public record WorkItemQueued(Guid WorkItemId, string Payload) : IIntegrationEvent;
