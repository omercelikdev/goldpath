namespace Goldpath;

/// <summary>
/// The escalation sweep as a Jobs run: overdue rungs move UP (and the top rung expires) on
/// a schedule instead of a hand-rolled loop. One chunk — the sweep is a single set-based
/// pass over the pending worklist, already idempotent (an escalated request is no longer
/// overdue), so Jobs' checkpoint/resume semantics apply trivially.
/// </summary>
public sealed class GoldpathApprovalEscalationJob : IGoldpathJob
{
    private readonly GoldpathApprovalEngine _engine;

    /// <summary>Resolved per fire.</summary>
    public GoldpathApprovalEscalationJob(GoldpathApprovalEngine engine) => _engine = engine;

    /// <inheritdoc />
    public Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, CancellationToken cancellationToken)
        => Task.FromResult(new GoldpathJobPlan(["sweep"], totalItems: null));

    /// <inheritdoc />
    public Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, CancellationToken cancellationToken)
        => _engine.EscalateOverdueAsync(cancellationToken);
}

/// <summary>Schedules the approvals runs on the Jobs module.</summary>
public static class GoldpathApprovalsJobsExtensions
{
    /// <summary>
    /// Schedules the escalation sweep (<see cref="GoldpathApprovalEngine.EscalateOverdueAsync"/>).
    /// Every five minutes by default: rung deadlines are measured in hours, so a five-minute
    /// sweep granularity never moves an SLA.
    /// </summary>
    public static GoldpathJobsOptions AddGoldpathApprovalsJobs(
        this GoldpathJobsOptions jobs,
        string escalationCron = "0 */5 * * * ?",
        TimeSpan? deadline = null)
    {
        jobs.AddJob<GoldpathApprovalEscalationJob>(j =>
        {
            j.Cron = escalationCron;
            j.Deadline = deadline ?? TimeSpan.FromMinutes(5);
            j.MaxParallelChunks = 1;   // one sweep, one pass
        });
        return jobs;
    }
}
