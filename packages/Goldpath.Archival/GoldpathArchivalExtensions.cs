using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>
/// Composition entry points. Declare the lifecycle with <c>AddGoldpathArchival</c>, map the
/// store with <c>modelBuilder.AddGoldpathArchiveModel()</c>, and schedule the runs through the
/// Jobs module with <c>jobs.AddGoldpathArchivalJobs&lt;TContext&gt;()</c> — the ladder composes
/// itself (archival RFC D3).
/// </summary>
public static class GoldpathArchivalExtensions
{
    /// <summary>Registers the archival engine and the declared lifecycle.</summary>
    public static TBuilder AddGoldpathArchival<TBuilder, TContext>(this TBuilder builder, Action<GoldpathArchivalOptions> configure)
        where TBuilder : IHostApplicationBuilder
        where TContext : DbContext
    {
        var options = new GoldpathArchivalOptions();
        builder.Configuration.GetSection("Goldpath:Archival").Bind(options);
        configure(options);
        builder.Services.AddSingleton(options);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<GoldpathArchiveEngine<TContext>>();
        builder.Services.TryAddSingleton<GoldpathArchiveAdminService<TContext>>();
        builder.Services.TryAddScoped<GoldpathArchiveJob<TContext>>();
        builder.Services.TryAddScoped<GoldpathRetentionPurgeJob<TContext>>();
        builder.Services.TryAddScoped<GoldpathArchiveVerifyJob<TContext>>();
        return builder;
    }

    /// <summary>
    /// Schedules the three archival runs on the Jobs module: archive (nightly), retention
    /// purge (nightly, AFTER the archive via chaining) and chain verification (weekly).
    /// The archive job pins MaxParallelChunks = 1 — the chain is single-writer by design.
    /// </summary>
    public static GoldpathJobsOptions AddGoldpathArchivalJobs<TContext>(
        this GoldpathJobsOptions jobs,
        string archiveCron = "0 0 2 * * ?",
        string verifyCron = "0 0 5 ? * SUN",
        TimeSpan? deadline = null)
        where TContext : DbContext
    {
        var slaDeadline = deadline ?? TimeSpan.FromHours(3);
        jobs.AddJob<GoldpathArchiveJob<TContext>>(j =>
        {
            j.Cron = archiveCron;
            j.Deadline = slaDeadline;
            j.MaxParallelChunks = 1;   // the hash chain appends single-writer
        });
        jobs.AddJob<GoldpathRetentionPurgeJob<TContext>>(j =>
        {
            j.Deadline = slaDeadline;
            j.MaxParallelChunks = 1;   // prefix purges walk the chain in order
            j.StartAfter<GoldpathArchiveJob<TContext>>();   // purge never races the archive
        });
        jobs.AddJob<GoldpathArchiveVerifyJob<TContext>>(j =>
        {
            j.Cron = verifyCron;
            j.Deadline = slaDeadline;
            j.MaxParallelChunks = 4;   // verification only reads — parallel is safe
        });
        return jobs;
    }
}
