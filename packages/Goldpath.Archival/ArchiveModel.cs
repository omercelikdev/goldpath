using Microsoft.EntityFrameworkCore;

namespace Goldpath;

/// <summary>
/// One archived aggregate: the serialized graph plus its place in the tamper-evident chain.
/// Entries are IMMUTABLE by contract — erasure redacts fields inside the document (D4),
/// verification recomputes hashes (D1); nothing ever updates an entry in place except the
/// audited erasure path, which re-stamps the content hash and records itself.
/// </summary>
public class GoldpathArchiveEntry
{
    /// <summary>Row id.</summary>
    public long Id { get; set; }

    /// <summary>The archive definition this entry belongs to.</summary>
    public string Definition { get; set; } = "";

    /// <summary>The aggregate's key (stringified — stable across key types).</summary>
    public string AggregateKey { get; set; } = "";

    /// <summary>Owning tenant, or null in single-tenant deployments.</summary>
    public string? Tenant { get; set; }

    /// <summary>The serialized graph (versioned JSON envelope payload).</summary>
    public string Document { get; set; } = "";

    /// <summary>Envelope schema version (never guess forward).</summary>
    public int SchemaVersion { get; set; }

    /// <summary>When the aggregate became due (its lifecycle event time).</summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>When the archive run captured it.</summary>
    public DateTimeOffset ArchivedAt { get; set; }

    /// <summary>This entry's position in the per-definition chain (dense, ascending).</summary>
    public long ChainIndex { get; set; }

    /// <summary>
    /// SHA-256 of the CURRENT canonical envelope. Re-stamped by the audited erasure path —
    /// diverging from <see cref="ChainHash"/> WITHOUT <see cref="ErasedAt"/> is tamper.
    /// </summary>
    public string ContentHash { get; set; } = "";

    /// <summary>
    /// SHA-256 sealed at APPEND time (content-hash-at-append + previous chain hash + index).
    /// Never changes — the chain links through this, so erasure redaction cannot break it.
    /// </summary>
    public string ChainHash { get; set; } = "";

    /// <summary>The previous entry's chain hash ("" for the genesis entry).</summary>
    public string PreviousHash { get; set; } = "";

    /// <summary>Set when an erasure redacted classified fields (the evidence pointer).</summary>
    public DateTimeOffset? ErasedAt { get; set; }
}

/// <summary>Per-definition chain head + purged-prefix bookkeeping (append is single-writer via Jobs).</summary>
public class GoldpathArchiveChainState
{
    /// <summary>The archive definition.</summary>
    public string Definition { get; set; } = "";

    /// <summary>Index of the newest entry.</summary>
    public long LastIndex { get; set; }

    /// <summary>Hash of the newest entry (the append anchor).</summary>
    public string LastHash { get; set; } = "";

    /// <summary>Retention purges remove the OLDEST prefix; verification starts after it.</summary>
    public long PurgedThroughIndex { get; set; }

    /// <summary>Hash of the last purged entry — the first kept entry must link to it.</summary>
    public string PurgedHeadHash { get; set; } = "";
}

/// <summary>An active or lifted legal hold — exempts purge AND hard erasure (D4).</summary>
public class GoldpathLegalHold
{
    /// <summary>Row id.</summary>
    public long Id { get; set; }

    /// <summary>The archive definition the hold scopes to.</summary>
    public string Definition { get; set; } = "";

    /// <summary>The held aggregate key.</summary>
    public string AggregateKey { get; set; } = "";

    /// <summary>The litigation/case reference that justifies the hold.</summary>
    public string CaseReference { get; set; } = "";

    /// <summary>Who placed it.</summary>
    public string PlacedBy { get; set; } = "";

    /// <summary>When it was placed.</summary>
    public DateTimeOffset PlacedAt { get; set; }

    /// <summary>Who lifted it (null while active).</summary>
    public string? LiftedBy { get; set; }

    /// <summary>When it was lifted (null while active).</summary>
    public DateTimeOffset? LiftedAt { get; set; }
}

/// <summary>The KVKK/GDPR evidence row: one erasure request and what it touched.</summary>
public class GoldpathErasureRecord
{
    /// <summary>Row id.</summary>
    public long Id { get; set; }

    /// <summary>The data subject's key (as the requester identified it).</summary>
    public string SubjectKey { get; set; } = "";

    /// <summary>Who executed the request.</summary>
    public string RequestedBy { get; set; } = "";

    /// <summary>When it executed.</summary>
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>How many archive entries were redacted.</summary>
    public int EntriesAffected { get; set; }

    /// <summary>Free-form context (ticket id, scope notes).</summary>
    public string? Detail { get; set; }
}

/// <summary>Model wiring for the archival tables.</summary>
public static class GoldpathArchiveModelExtensions
{
    /// <summary>
    /// Maps the archive store (entries + chain state), legal holds and erasure evidence —
    /// one call in <c>OnModelCreating</c>, everything rides normal migrations.
    /// </summary>
    public static ModelBuilder AddGoldpathArchiveModel(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GoldpathArchiveEntry>(entity =>
        {
            entity.ToTable("GoldpathArchiveEntries");
            entity.Property(e => e.Definition).HasMaxLength(120);
            entity.Property(e => e.AggregateKey).HasMaxLength(256);
            entity.Property(e => e.Tenant).HasMaxLength(64);
            entity.Property(e => e.ContentHash).HasMaxLength(64);
            entity.Property(e => e.ChainHash).HasMaxLength(64);
            entity.Property(e => e.PreviousHash).HasMaxLength(64);
            // The retrieval budget (finance: p95 < 5s at 1M entries) rides this index.
            entity.HasIndex(e => new { e.Definition, e.AggregateKey }).IsUnique();
            entity.HasIndex(e => new { e.Definition, e.ChainIndex }).IsUnique();
            entity.HasIndex(e => new { e.Definition, e.ArchivedAt });
        });

        modelBuilder.Entity<GoldpathArchiveChainState>(entity =>
        {
            entity.ToTable("GoldpathArchiveChainState");
            entity.HasKey(e => e.Definition);
            entity.Property(e => e.Definition).HasMaxLength(120);
            entity.Property(e => e.LastHash).HasMaxLength(64);
            entity.Property(e => e.PurgedHeadHash).HasMaxLength(64);
        });

        modelBuilder.Entity<GoldpathLegalHold>(entity =>
        {
            entity.ToTable("GoldpathLegalHolds");
            entity.Property(e => e.Definition).HasMaxLength(120);
            entity.Property(e => e.AggregateKey).HasMaxLength(256);
            entity.Property(e => e.CaseReference).HasMaxLength(256);
            entity.Property(e => e.PlacedBy).HasMaxLength(256);
            entity.Property(e => e.LiftedBy).HasMaxLength(256);
            entity.HasIndex(e => new { e.Definition, e.AggregateKey, e.LiftedAt });
        });

        modelBuilder.Entity<GoldpathErasureRecord>(entity =>
        {
            entity.ToTable("GoldpathErasureRecords");
            entity.Property(e => e.SubjectKey).HasMaxLength(256);
            entity.Property(e => e.RequestedBy).HasMaxLength(256);
            entity.HasIndex(e => e.SubjectKey);
        });

        return modelBuilder;
    }
}
