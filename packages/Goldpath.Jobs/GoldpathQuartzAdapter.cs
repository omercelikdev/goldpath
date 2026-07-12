using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Goldpath;

/// <summary>
/// The bridge between one Quartz fire and one Goldpath run. Generic ON PURPOSE: the closed type
/// is the stored <c>job_class_name</c>, so every registered job stays unique in the store,
/// and a scheduler never fires a class its own assembly lacks (jobs RFC D9: one cluster per
/// worker kind makes that structural).
/// </summary>
/// <remarks>
/// <see cref="DisallowConcurrentExecutionAttribute"/> + the clustered store = at most one
/// fire of this job ANYWHERE in the cluster; recovery re-fires on a healthy node and the
/// runner resumes from the checkpoint.
/// </remarks>
[DisallowConcurrentExecution]
internal sealed class GoldpathQuartzAdapter<TJob> : IJob
    where TJob : class, IGoldpathJob
{
    private readonly TJob _job;
    private readonly IGoldpathJobRunner _runner;
    private readonly GoldpathJobsOptions _options;

    /// <summary>Resolved per fire from the scoped container.</summary>
    public GoldpathQuartzAdapter(TJob job, IGoldpathJobRunner runner, GoldpathJobsOptions options)
    {
        _job = job;
        _runner = runner;
        _options = options;
    }

    /// <inheritdoc />
    public async Task Execute(IJobExecutionContext context)
    {
        var definition = _options.Jobs.First(d => d.JobType == typeof(TJob));
        // The trigger/replay verbs stamp the caller's traceparent into the data map —
        // the only vehicle that crosses the Quartz store between request and fire.
        var traceParent = context.MergedJobDataMap.TryGetValue(GoldpathJobsExtensions.TraceParentKey, out var stamped)
            ? stamped?.ToString()
            : null;
        var fire = new GoldpathFireFacts(
            context.Scheduler.SchedulerName,
            context.Scheduler.SchedulerInstanceId,
            context.FireInstanceId,
            context.Recovering,
            traceParent);

        try
        {
            if (context.MergedJobDataMap.TryGetValue(GoldpathJobsExtensions.ReplayRunKey, out var raw)
                && Guid.TryParse(raw?.ToString(), out var replayRunId))
            {
                await _runner.ReplayAsync(_job, replayRunId, fire, context.CancellationToken);
                return;
            }

            var status = await _runner.RunAsync(_job, definition, fire, context.CancellationToken);

            if (status == GoldpathJobRunStatus.Completed)
            {
                // Chaining v1 (jobs RFC D6): completion triggers every job registered
                // with StartAfter<this>. Ad-hoc fire through the scheduler — the chained
                // run gets its own fire, its own recovery, its own history.
                foreach (var successor in _options.Jobs.Where(d => d.StartAfterJobs.Contains(definition.Name)))
                {
                    await context.Scheduler.TriggerJob(new JobKey(successor.Name, GoldpathJobsExtensions.JobGroup), context.CancellationToken);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Quartz must SEE the failure (history, refire policy) but never refire blind.
            throw new JobExecutionException(exception, refireImmediately: false);
        }
    }
}
