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

        // The clustered Quartz store AND the run model (runs/chunks/repair/history/audit)
        // ride normal migrations — one call, no side-channel SQL (jobs RFC D2).
        modelBuilder.AddGoldpathJobs();
    }
}
