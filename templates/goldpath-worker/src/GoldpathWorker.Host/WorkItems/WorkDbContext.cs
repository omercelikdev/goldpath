using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace GoldpathWorker.Host.WorkItems;

public class WorkDbContext(DbContextOptions<WorkDbContext> options) : DbContext(options)
{
    public DbSet<ProcessedWorkItem> ProcessedWorkItems => Set<ProcessedWorkItem>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.ApplyGoldpathConventions();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyGoldpathModelDefaults();
        // goldpath:features model — the drift profile is the source of these rows
#if (UseAuditTrail)
        modelBuilder.AddGoldpathAuditLog();
#endif
#if (UseFileExchange)
        modelBuilder.AddGoldpathFileExchangeModel();  // processed keys + quarantine + archive marks
#endif
#if (UseSoftDelete)
        modelBuilder.ApplyGoldpathSoftDelete();
#endif
#if (UseMultiTenancy)
        modelBuilder.ApplyGoldpathMultiTenancy(this);   // context-rooted ON PURPOSE — keeps the filter live
#endif
#if (UseNotification)
        modelBuilder.AddGoldpathNotification();   // evidence rows + attachments
#endif
#if (UseNotification || UseFileExchange)
        modelBuilder.AddGoldpathJobs();           // run model + clustered Quartz store (same database as the inbox)
#endif

        // Inbox/outbox tables: the consumer-side dedup store (exactly-once processing).
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
