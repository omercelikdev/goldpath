namespace Goldpath;

/// <summary>
/// A Goldpath job: chunk-shaped ON PURPOSE — the runner can only checkpoint what the job exposes
/// as steps. Plan the work once (set-based, keyset-paged discovery), then execute it chunk
/// by chunk; after every successful chunk the runner persists a checkpoint, so a crashed or
/// killed run RESUMES instead of restarting (jobs RFC §2).
/// </summary>
public interface IGoldpathJob
{
    /// <summary>
    /// Discovers the work and splits it into chunks. Runs once per run; runs AGAIN on a
    /// resumed run only if the original plan was never persisted. Keep it cheap and paged —
    /// materializing the full item list here is analyzer-flagged (GP0503).
    /// </summary>
    Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Executes ONE chunk. The runner marks the chunk complete (the checkpoint) only after
    /// this returns; throwing retries the chunk up to the configured attempts, then isolates
    /// it as failed while the rest of the run continues. Report per-item failures via
    /// <see cref="GoldpathJobChunk.ReportItemFailure"/> — they land in the repair queue without
    /// failing the chunk.
    /// </summary>
    Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Opt-in hook for the admin replay verb: how to process ONE previously-failed item. The
/// hook runs on an EXECUTOR (the type lives there); jobs without it refuse replay loudly.
/// </summary>
public interface IGoldpathItemReplay
{
    /// <summary>Processes one repair-queue item; throwing keeps the item open.</summary>
    Task ReplayItemAsync(string itemKey, GoldpathJobContext context, CancellationToken cancellationToken);
}

/// <summary>The planned shape of a run: chunk payloads plus an optional total-item count.</summary>
public sealed class GoldpathJobPlan
{
    /// <summary>Creates a plan from opaque chunk payloads (e.g. keyset ranges).</summary>
    public GoldpathJobPlan(IReadOnlyList<string> chunkPayloads, long? totalItems = null)
    {
        ChunkPayloads = chunkPayloads;
        TotalItems = totalItems;
    }

    /// <summary>One payload per chunk — the job's own cursor/range encoding, never item lists.</summary>
    public IReadOnlyList<string> ChunkPayloads { get; }

    /// <summary>Total items across the run (progress/ETA math), when the job knows it.</summary>
    public long? TotalItems { get; }
}

/// <summary>One unit of checkpointable work handed to <see cref="IGoldpathJob.ExecuteChunkAsync"/>.</summary>
public sealed class GoldpathJobChunk
{
    internal GoldpathJobChunk(int index, string payload)
    {
        Index = index;
        Payload = payload;
    }

    /// <summary>Zero-based position of the chunk in the plan.</summary>
    public int Index { get; }

    /// <summary>The payload the job planned for this chunk.</summary>
    public string Payload { get; }

    internal List<(string ItemKey, string Reason)> ItemFailures { get; } = [];

    /// <summary>
    /// Isolates ONE failed item into the repair queue without failing the chunk — a single
    /// poisoned item must never stop the night's run (scenario-card rule).
    /// </summary>
    public void ReportItemFailure(string itemKey, string reason)
        => ItemFailures.Add((itemKey, reason));
}

/// <summary>Ambient facts of the current run, available to both job phases.</summary>
public sealed class GoldpathJobContext
{
    internal GoldpathJobContext(Guid runId, string schedulerName, string instanceName, string jobName, bool resumed, string? inputVersion, IServiceProvider services)
    {
        RunId = runId;
        SchedulerName = schedulerName;
        InstanceName = instanceName;
        JobName = jobName;
        Resumed = resumed;
        InputVersion = inputVersion;
        Services = services;
    }

    /// <summary>The run identity (correlates chunks, failures and history).</summary>
    public Guid RunId { get; }

    /// <summary>The owning scheduler (one per worker kind — jobs RFC D9).</summary>
    public string SchedulerName { get; }

    /// <summary>The cluster instance executing THIS chunk (resume may move between nodes).</summary>
    public string InstanceName { get; }

    /// <summary>The registered job name.</summary>
    public string JobName { get; }

    /// <summary>True when this execution continues an interrupted run from its checkpoint.</summary>
    public bool Resumed { get; }

    /// <summary>The input version pinned at run start (jobs RFC: mid-run deploys never mix inputs).</summary>
    public string? InputVersion { get; }

    /// <summary>Scoped services for the executing chunk.</summary>
    public IServiceProvider Services { get; }
}
