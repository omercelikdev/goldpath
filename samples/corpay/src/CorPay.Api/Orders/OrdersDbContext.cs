using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace CorPay.Api.Orders;

public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Payments.PaymentInstruction> PaymentInstructions => Set<Payments.PaymentInstruction>();

    public DbSet<Payments.LedgerFeedEntry> LedgerFeed => Set<Payments.LedgerFeedEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.ApplyGoldpathConventions();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyGoldpathModelDefaults();
        // goldpath:features model — the drift profile is the source of these rows
        modelBuilder.AddGoldpathAuditLog();
        modelBuilder.ApplyGoldpathSoftDelete();
        modelBuilder.ApplyGoldpathMultiTenancy(this);   // context-rooted ON PURPOSE — keeps the filter live
        modelBuilder.AddGoldpathArchiveModel();   // archive entries + chain state + holds + erasure evidence
        modelBuilder.AddGoldpathBulk();           // files + batches + rows + value-free report
        modelBuilder.AddGoldpathJobs();           // run model + clustered Quartz store (same database)

        // Transactional outbox/inbox tables (features.outbox in the manifest).
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
