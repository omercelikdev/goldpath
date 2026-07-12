using MassTransit;

namespace GoldpathWorker.Host.WorkItems;

/// <summary>
/// The walking-skeleton consumer: inbox-guarded (exactly-once), commits its result in the
/// same transaction as the dedup bookkeeping. Replace the body with the real work.
/// </summary>
public class WorkItemQueuedConsumer(WorkDbContext db) : IConsumer<WorkItemQueued>
{
    /// <inheritdoc />
    public async Task Consume(ConsumeContext<WorkItemQueued> context)
    {
        db.ProcessedWorkItems.Add(new ProcessedWorkItem
        {
            Id = context.Message.WorkItemId,
            Payload = context.Message.Payload,
            ProcessedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
