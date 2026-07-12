using Microsoft.EntityFrameworkCore;

namespace Goldpath;

/// <summary>Lifecycle of a run (string-typed in the store for cross-provider readability).</summary>
public static class GoldpathJobRunStatus
{
    /// <summary>Chunks are executing (or the run is interrupted and awaiting resume).</summary>
    public const string Running = "Running";

    /// <summary>Every chunk completed.</summary>
    public const string Completed = "Completed";

    /// <summary>All chunks finished but at least one exhausted its attempts.</summary>
    public const string Failed = "Failed";
}

/// <summary>Chunk states — the checkpoint vocabulary.</summary>
public static class GoldpathJobChunkStatus
{
    /// <summary>Waiting to be claimed.</summary>
    public const string Pending = "Pending";

    /// <summary>Claimed by an executing worker (stale claims reset on resume).</summary>
    public const string Claimed = "Claimed";

    /// <summary>Done — THE checkpoint: a resumed run never re-executes these.</summary>
    public const string Completed = "Completed";

    /// <summary>Attempts exhausted; isolated so the rest of the run continues.</summary>
    public const string Failed = "Failed";
}

/// <summary>One execution of one job: the ladder-wide progress/history record (jobs RFC §2).</summary>
public class GoldpathJobRun
{
    /// <summary>Run id.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning scheduler (one per worker kind).</summary>
    public string SchedulerName { get; set; } = "";

    /// <summary>The registered job name.</summary>
    public string JobName { get; set; } = "";

    /// <summary>See <see cref="GoldpathJobRunStatus"/>.</summary>
    public string Status { get; set; } = GoldpathJobRunStatus.Running;

    /// <summary>When the run started (UTC).</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the run reached a terminal status.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Deadline derived from the job definition at start (prediction compares to this).</summary>
    public DateTimeOffset? DeadlineAt { get; set; }

    /// <summary>Live prediction from the checkpoint rate — alerts fire BEFORE the deadline does.</summary>
    public DateTimeOffset? PredictedFinishAt { get; set; }

    /// <summary>Chunks planned.</summary>
    public int TotalChunks { get; set; }

    /// <summary>Chunks completed (the progress numerator).</summary>
    public int CompletedChunks { get; set; }

    /// <summary>Chunks that exhausted their attempts.</summary>
    public int FailedChunks { get; set; }

    /// <summary>Total items when the plan knew it.</summary>
    public long? TotalItems { get; set; }

    /// <summary>Items isolated into the repair queue.</summary>
    public int ItemFailures { get; set; }

    /// <summary>The instance that STARTED the run (resume may continue elsewhere).</summary>
    public string? StartedBy { get; set; }

    /// <summary>Pinned input version (mid-run deploys never mix inputs).</summary>
    public string? InputVersion { get; set; }

    /// <summary>How many executions (fires) this run consumed — 1 normally, more after recovery.</summary>
    public int Executions { get; set; }
}

/// <summary>One checkpointable chunk row. Claim/complete transitions ARE the checkpoint.</summary>
public class GoldpathJobRunChunk
{
    /// <summary>Row id.</summary>
    public long Id { get; set; }

    /// <summary>Owning run.</summary>
    public Guid RunId { get; set; }

    /// <summary>Zero-based plan position.</summary>
    public int Index { get; set; }

    /// <summary>The job's payload for this chunk.</summary>
    public string Payload { get; set; } = "";

    /// <summary>See <see cref="GoldpathJobChunkStatus"/>. Concurrency token: claims race safely.</summary>
    public string Status { get; set; } = GoldpathJobChunkStatus.Pending;

    /// <summary>Execution attempts so far.</summary>
    public int Attempts { get; set; }

    /// <summary>Instance holding the claim.</summary>
    public string? ClaimedBy { get; set; }

    /// <summary>Quartz fire instance the claim belongs to (stale claims from dead fires reset).</summary>
    public string? FireInstanceId { get; set; }

    /// <summary>When the claim was taken.</summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>When the chunk completed (checkpoint time).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Last error when attempts failed.</summary>
    public string? LastError { get; set; }
}

/// <summary>One isolated item failure — the repair queue (redrive verbs arrive in S2).</summary>
public class GoldpathJobItemFailure
{
    /// <summary>Row id.</summary>
    public long Id { get; set; }

    /// <summary>Owning run.</summary>
    public Guid RunId { get; set; }

    /// <summary>Chunk the item belonged to.</summary>
    public int ChunkIndex { get; set; }

    /// <summary>The job's key for the failed item.</summary>
    public string ItemKey { get; set; } = "";

    /// <summary>Why it failed (teaches the repair).</summary>
    public string Reason { get; set; } = "";

    /// <summary>When it was isolated.</summary>
    public DateTimeOffset FailedAt { get; set; }

    /// <summary>When the item was replayed through the admin verb (null = still open).</summary>
    public DateTimeOffset? RedrivenAt { get; set; }
}

/// <summary>
/// One admin action — §7.1 iron rule 2: no admin action without an audit record. Written by
/// the admin service for EVERY mutating verb (who / when / what / against which fleet).
/// </summary>
public class GoldpathJobAdminAudit
{
    /// <summary>Row id.</summary>
    public long Id { get; set; }

    /// <summary>When the verb executed (UTC).</summary>
    public DateTimeOffset At { get; set; }

    /// <summary>Who invoked it (auth principal name; "anonymous" only outside the auth floor).</summary>
    public string Actor { get; set; } = "";

    /// <summary>The verb (trigger, pause, resume, reschedule, rerun, replay-item, calendar-*, pause-all, resume-all).</summary>
    public string Action { get; set; } = "";

    /// <summary>The fleet (scheduler name) the verb targeted.</summary>
    public string Fleet { get; set; } = "";

    /// <summary>The job/calendar/run the verb targeted.</summary>
    public string Target { get; set; } = "";

    /// <summary>Verb-specific detail (new cron, item key, dry-run flag...).</summary>
    public string? Detail { get; set; }
}

/// <summary>
/// One fire of one job across the cluster — who ran it, how long, how it ended
/// (Completed/Failed/Vetoed/Recovered). A battle-proven execution-history listener pattern.
/// </summary>
public class GoldpathJobExecution
{
    /// <summary>Row id.</summary>
    public long Id { get; set; }

    /// <summary>Owning scheduler.</summary>
    public string SchedulerName { get; set; } = "";

    /// <summary>The job name.</summary>
    public string JobName { get; set; } = "";

    /// <summary>The run this fire served, when known.</summary>
    public Guid? RunId { get; set; }

    /// <summary>Cluster node that executed the fire.</summary>
    public string InstanceName { get; set; } = "";

    /// <summary>When the fire started (UTC).</summary>
    public DateTimeOffset FiredAt { get; set; }

    /// <summary>When it finished.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Duration in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>Completed | Failed | Vetoed | Recovered.</summary>
    public string Outcome { get; set; } = "";

    /// <summary>Error message when the fire failed.</summary>
    public string? Error { get; set; }
}

/// <summary>Model wiring: the run model AND the clustered Quartz store schema.</summary>
public static class GoldpathJobsModelExtensions
{
    /// <summary>
    /// Maps the Goldpath run model (runs/chunks/item failures/executions) and the Quartz
    /// persistent-store tables into the context — ONE call in <c>OnModelCreating</c>, and
    /// the whole jobs story lives inside normal EF migration discipline (jobs RFC D2).
    /// </summary>
    public static ModelBuilder AddGoldpathJobs(this ModelBuilder modelBuilder, bool excludeFromMigrations = false)
        => modelBuilder.AddGoldpathContribution(excludeFromMigrations, Map);

    private static void Map(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GoldpathJobRun>(entity =>
        {
            entity.ToTable("GoldpathJobRuns");
            entity.Property(e => e.SchedulerName).HasMaxLength(120);
            entity.Property(e => e.JobName).HasMaxLength(150);
            entity.Property(e => e.Status).HasMaxLength(16);
            entity.HasIndex(e => new { e.SchedulerName, e.JobName, e.Status });
            entity.HasIndex(e => e.StartedAt);
        });

        modelBuilder.Entity<GoldpathJobRunChunk>(entity =>
        {
            entity.ToTable("GoldpathJobRunChunks");
            entity.Property(e => e.Status).HasMaxLength(16).IsConcurrencyToken();
            entity.HasIndex(e => new { e.RunId, e.Status });
            entity.HasIndex(e => new { e.RunId, e.Index }).IsUnique();
        });

        modelBuilder.Entity<GoldpathJobItemFailure>(entity =>
        {
            entity.ToTable("GoldpathJobItemFailures");
            entity.Property(e => e.ItemKey).HasMaxLength(256);
            entity.HasIndex(e => e.RunId);
        });

        modelBuilder.Entity<GoldpathJobAdminAudit>(entity =>
        {
            entity.ToTable("GoldpathJobAdminAudit");
            entity.Property(e => e.Actor).HasMaxLength(256);
            entity.Property(e => e.Action).HasMaxLength(32);
            entity.Property(e => e.Fleet).HasMaxLength(120);
            entity.Property(e => e.Target).HasMaxLength(256);
            entity.HasIndex(e => e.At);
        });

        modelBuilder.Entity<GoldpathJobExecution>(entity =>
        {
            entity.ToTable("GoldpathJobExecutions");
            entity.Property(e => e.SchedulerName).HasMaxLength(120);
            entity.Property(e => e.JobName).HasMaxLength(150);
            entity.Property(e => e.Outcome).HasMaxLength(16);
            entity.HasIndex(e => new { e.SchedulerName, e.JobName });
            entity.HasIndex(e => e.FiredAt);
        });

        modelBuilder.AddQuartzStoreTables();
    }
}
