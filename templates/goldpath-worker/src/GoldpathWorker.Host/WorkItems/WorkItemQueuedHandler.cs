namespace GoldpathWorker.Host.WorkItems;

/// <summary>
/// The walking-skeleton handler: inbox-guarded (exactly-once), commits its result in the
/// same transaction as the dedup bookkeeping. Through the seam (ADR-0013): no bus type
/// here; the queue is named after the handler (work-item-queued). Replace the body with
/// the real work.
/// </summary>
public class WorkItemQueuedHandler(WorkDbContext db) : IIntegrationEventHandler<WorkItemQueued>
{
    /// <inheritdoc />
    public async Task HandleAsync(WorkItemQueued integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken = default)
    {
        db.ProcessedWorkItems.Add(new ProcessedWorkItem
        {
            Id = integrationEvent.WorkItemId,
            Payload = integrationEvent.Payload,
            ProcessedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
