using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Listener;

namespace Goldpath;

/// <summary>
/// Records every fire into <see cref="GoldpathJobExecution"/> — who ran it, where, how long, how
/// it ended (Completed/Failed/Vetoed/Recovered). Cluster-wide history is table stakes for
/// the ops console (a battle-proven pattern). Never throws into the scheduler: a history
/// hiccup must not fail a job.
/// </summary>
internal sealed class GoldpathJobHistoryListener<TContext> : JobListenerSupport
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _time;
    private readonly ILogger<GoldpathJobHistoryListener<TContext>> _logger;

    /// <summary>Created once per host (registered by <c>AddGoldpathJobs</c>).</summary>
    public GoldpathJobHistoryListener(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<GoldpathJobHistoryListener<TContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public override string Name => "goldpath-job-history";

    /// <inheritdoc />
    public override Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        var outcome = jobException is not null ? "Failed"
            : context.Recovering ? "Recovered"
            : "Completed";
        return RecordAsync(context, outcome, jobException?.GetBaseException().Message, cancellationToken);
    }

    /// <inheritdoc />
    public override Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
        => RecordAsync(context, "Vetoed", error: null, cancellationToken);

    private async Task RecordAsync(IJobExecutionContext context, string outcome, string? error, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            db.Add(new GoldpathJobExecution
            {
                SchedulerName = context.Scheduler.SchedulerName,
                JobName = context.JobDetail.Key.Name,
                InstanceName = context.Scheduler.SchedulerInstanceId,
                FiredAt = context.FireTimeUtc,
                FinishedAt = _time.GetUtcNow(),
                DurationMs = (long)context.JobRunTime.TotalMilliseconds,
                Outcome = outcome,
                Error = error,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to record job execution history for {Job}.", context.JobDetail.Key);
        }
    }
}
