using Microsoft.EntityFrameworkCore;

namespace GoldpathWorker.Host.Reports;

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

        // The clustered Quartz store AND the run model (runs/chunks/repair/history/audit)
        // ride normal migrations — one call, no side-channel SQL (jobs RFC D2).
        modelBuilder.AddGoldpathJobs();
    }
}
