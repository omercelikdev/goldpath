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

        // The money rule the DATABASE enforces: one reference pays once per tenant. The
        // read-before-write checks in both intakes are the fast path; under a parallel
        // race the loser hits this index — bulk's loser lands in the repair queue and
        // replay heals it quietly (the row already exists), single-submit's client
        // retries through the idempotency middleware.
        modelBuilder.Entity<Payments.PaymentInstruction>()
            .HasIndex(i => new { i.TenantId, i.Reference })
            .IsUnique();
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
