using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>
/// Composition entry points. Declare templates + channels with <c>AddGoldpathNotification</c>,
/// map the store with <c>modelBuilder.AddGoldpathNotification()</c>, and schedule the runs
/// through the Jobs module with <c>jobs.AddGoldpathNotificationJobs&lt;TContext&gt;()</c> —
/// the ladder composes itself a third time (notification RFC D3).
/// </summary>
public static class GoldpathNotificationExtensions
{
    /// <summary>Registers the notifier, templates and the shipped channels.</summary>
    public static TBuilder AddGoldpathNotification<TBuilder, TContext>(this TBuilder builder, Action<GoldpathNotificationOptions> configure)
        where TBuilder : IHostApplicationBuilder
        where TContext : DbContext
    {
        var options = new GoldpathNotificationOptions();
        builder.Configuration.GetSection("Goldpath:Notification:Email").Bind(options.EmailOptions);
        builder.Configuration.GetSection("Goldpath:Notification:Webhook").Bind(options.WebhookOptions);
        configure(options);
        builder.Services.AddSingleton(options);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient("goldpath-notification-webhook");
        builder.Services.TryAddScoped<IGoldpathNotifier, GoldpathNotifier<TContext>>();
        builder.Services.TryAddSingleton<GoldpathNotificationAdminService<TContext>>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IGoldpathNotificationChannel, GoldpathEmailChannel>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IGoldpathNotificationChannel, GoldpathWebhookChannel>());
        builder.Services.TryAddScoped<GoldpathNotificationSendJob<TContext>>();
        builder.Services.TryAddScoped<GoldpathNotificationRetentionJob<TContext>>();
        return builder;
    }

    /// <summary>
    /// Schedules the two notification runs on the Jobs module: send (frequent — a stuck
    /// queue pages before customers call; the cron IS the trigger, notification works in
    /// no-broker apps) and body retention (nightly).
    /// </summary>
    public static GoldpathJobsOptions AddGoldpathNotificationJobs<TContext>(
        this GoldpathJobsOptions jobs,
        string sendCron = "0/30 * * * * ?",
        string retentionCron = "0 30 3 * * ?",
        TimeSpan? deadline = null)
        where TContext : DbContext
    {
        var slaDeadline = deadline ?? TimeSpan.FromMinutes(30);
        jobs.AddJob<GoldpathNotificationSendJob<TContext>>(j =>
        {
            j.Cron = sendCron;
            j.Deadline = slaDeadline;
            // MaxParallelChunks stays 1 (the jobs default): the claim query is
            // keyset-simple on purpose; parallel sending is an explicit opt-in.
        });
        jobs.AddJob<GoldpathNotificationRetentionJob<TContext>>(j =>
        {
            j.Cron = retentionCron;
            j.Deadline = slaDeadline;
        });
        return jobs;
    }
}
