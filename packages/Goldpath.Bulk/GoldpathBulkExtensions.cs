using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>
/// Composition entry points. Declare batch shapes with <c>AddGoldpathBulk</c>, map the store
/// with <c>modelBuilder.AddGoldpathBulk()</c>, register row handlers, and schedule the two runs
/// through the Jobs module with <c>jobs.AddGoldpathBulkJobs&lt;TContext&gt;()</c> — the ladder
/// composes itself again (bulk RFC D4).
/// </summary>
public static class GoldpathBulkExtensions
{
    /// <summary>Registers the bulk engine and the declared batch shapes.</summary>
    public static TBuilder AddGoldpathBulk<TBuilder, TContext>(this TBuilder builder, Action<GoldpathBulkOptions> configure)
        where TBuilder : IHostApplicationBuilder
        where TContext : DbContext
    {
        var options = new GoldpathBulkOptions();
        builder.Configuration.GetSection("Goldpath:Bulk").Bind(options);
        configure(options);
        builder.Services.AddSingleton(options);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<GoldpathBulkFileStore<TContext>>();
        builder.Services.TryAddSingleton<GoldpathBulkEngine<TContext>>();
        builder.Services.TryAddSingleton<GoldpathBulkAdminService<TContext>>();
        builder.Services.TryAddScoped<GoldpathBulkValidateJob<TContext>>();
        builder.Services.TryAddScoped<GoldpathBulkExecuteJob<TContext>>();
        return builder;
    }

    /// <summary>
    /// Schedules the two bulk runs on the Jobs module: validate (frequent — an uploaded
    /// file should not wait long for its report; the S2 upload verb also fires it
    /// immediately) and execute (picks up Approved batches). Both are cron SAFETY NETS:
    /// polling the state machine makes the pipeline self-healing after any outage.
    /// </summary>
    public static GoldpathJobsOptions AddGoldpathBulkJobs<TContext>(
        this GoldpathJobsOptions jobs,
        string validateCron = "0 * * * * ?",
        string executeCron = "30 * * * * ?",
        TimeSpan? deadline = null)
        where TContext : DbContext
    {
        var slaDeadline = deadline ?? TimeSpan.FromHours(2);
        jobs.AddJob<GoldpathBulkValidateJob<TContext>>(j =>
        {
            j.Cron = validateCron;
            j.Deadline = slaDeadline;
        });
        jobs.AddJob<GoldpathBulkExecuteJob<TContext>>(j =>
        {
            j.Cron = executeCron;
            j.Deadline = slaDeadline;
            // MaxParallelChunks stays 1 (the jobs default): payment-shaped batches care
            // about order; parallel execution is an explicit opt-in per app.
        });
        return jobs;
    }
}
