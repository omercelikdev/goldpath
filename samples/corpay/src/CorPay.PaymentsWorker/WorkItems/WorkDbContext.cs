using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace CorPay.PaymentsWorker.WorkItems;

public class WorkDbContext(DbContextOptions<WorkDbContext> options) : DbContext(options)
{
    public DbSet<ProcessedWorkItem> ProcessedWorkItems => Set<ProcessedWorkItem>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.ApplyGoldpathConventions();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyGoldpathModelDefaults();

        // Inbox/outbox tables: the consumer-side dedup store (exactly-once processing).
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
