#if (UseBroker)
using MassTransit;
#endif
using Microsoft.EntityFrameworkCore;

namespace GoldpathTemplate.Api.Orders;

public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.ApplyGoldpathConventions();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyGoldpathModelDefaults();
        // goldpath:features model — the drift profile is the source of these rows
#if (UseAuditTrail)
        modelBuilder.AddGoldpathAuditLog();
#endif
#if (UseSoftDelete)
        modelBuilder.ApplyGoldpathSoftDelete();
#endif
#if (UseMultiTenancy)
        modelBuilder.ApplyGoldpathMultiTenancy(this);   // context-rooted ON PURPOSE — keeps the filter live
#endif
#if (UseArchival)
        modelBuilder.AddGoldpathArchiveModel();   // archive entries + chain state + holds + erasure evidence
#endif
#if (UseBulk)
        modelBuilder.AddGoldpathBulk();           // files + batches + rows + value-free report
#endif
#if (UseNotification)
        modelBuilder.AddGoldpathNotification();   // evidence rows + attachments
#endif
#if (UseCampaign)
        modelBuilder.AddGoldpathCampaign();       // campaigns + items (the 30M-row table) + verb audit
#endif
#if (UseArchival || UseBulk || UseNotification || UseCampaign)
        modelBuilder.AddGoldpathJobs();           // run model + clustered Quartz store (same database)
#endif
#if (UseBroker)

        // Transactional outbox/inbox tables (features.outbox in the manifest).
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
#endif
    }
}
