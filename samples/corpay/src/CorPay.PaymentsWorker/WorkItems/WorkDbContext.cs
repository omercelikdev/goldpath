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
        // Mapped for RUNTIME use only — the API's context OWNS their DDL (migrations D3:
        // one table set, one migration owner). Without the exclusion this head's migration
        // re-created "InboxState" and crashed the first time anything applied it (T12's
        // quieter half: these migrations had never been applied anywhere).
        modelBuilder.AddGoldpathContribution(excludeFromMigrations: true, contribute =>
        {
            contribute.AddInboxStateEntity();
            contribute.AddOutboxMessageEntity();
            contribute.AddOutboxStateEntity();
        });
    }
}
