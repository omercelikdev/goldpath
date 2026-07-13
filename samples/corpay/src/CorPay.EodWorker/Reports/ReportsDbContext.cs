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

        // SHARED tables with the Api's fleet: the SchedulerName in Program.cs keeps
        // the clusters apart, and the API'S context OWNS their migrations — this head
        // maps them for querying only (one table set, ONE owner: migrations RFC D3).
        modelBuilder.AddGoldpathJobs(excludeFromMigrations: true);
    }
}
