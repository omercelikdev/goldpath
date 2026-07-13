namespace CorPay.PaymentsWorker.WorkItems;

/// <summary>
/// The broker-bound contract this worker drains (implements <c>IIntegrationEvent</c> —
/// GP0401). Rename/replace it with the real upstream event.
/// </summary>
public record WorkItemQueued(Guid WorkItemId, string Payload) : IIntegrationEvent;
