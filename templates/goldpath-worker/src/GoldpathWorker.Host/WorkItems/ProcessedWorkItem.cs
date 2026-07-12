namespace GoldpathWorker.Host.WorkItems;

/// <summary>The durable result of one processed message — the walking skeleton's "work done" proof.</summary>
public class ProcessedWorkItem
{
    /// <summary>The upstream work-item identity (also the primary key: a natural dedup backstop).</summary>
    public Guid Id { get; set; }

    /// <summary>What was processed.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>When processing committed (UTC policy: DateTimeOffset).</summary>
    public DateTimeOffset ProcessedAt { get; set; }
}
