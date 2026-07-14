using Microsoft.EntityFrameworkCore;

namespace CorPay.EodWorker.Reports;

public class ReportsDbContext(DbContextOptions<ReportsDbContext> options) : DbContext(options)
{
    public DbSet<DailyReportRow> DailyReports => Set<DailyReportRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.ApplyGoldpathConventions();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyGoldpathModelDefaults();
        modelBuilder.Entity<DailyReportRow>(report =>
        {
            report.HasKey(r => r.DayOffset);
            // The key IS the day — never an identity (day 0 must not become "generated").
            report.Property(r => r.DayOffset).ValueGeneratedNever();
        });

        // Slice 5 — the EOD read side. The Api's context OWNS these tables; this head
        // maps them for QUERYING only (one table set, ONE owner: migrations RFC D3).
        modelBuilder.Entity<PaymentInstructionRead>(read =>
        {
            read.ToTable("PaymentInstructions", t => t.ExcludeFromMigrations());
            read.Property(r => r.Status).HasMaxLength(256);
        });
        modelBuilder.Entity<LedgerFeedRead>(read => read.ToTable("LedgerFeed", t => t.ExcludeFromMigrations()));

        // The reconciliation evidence is THIS worker's own table (its migrations).
        modelBuilder.Entity<EodReconciliationRow>(row =>
        {
            row.Property(r => r.TenantId).HasMaxLength(128);
            row.HasIndex(r => new { r.Day, r.TenantId });
        });

        // SHARED tables with the Api's fleet: the SchedulerName in Program.cs keeps
        // the clusters apart, and the API'S context OWNS their migrations — this head
        // maps them for querying only (one table set, ONE owner: migrations RFC D3).
        modelBuilder.AddGoldpathJobs(excludeFromMigrations: true);
    }
}
