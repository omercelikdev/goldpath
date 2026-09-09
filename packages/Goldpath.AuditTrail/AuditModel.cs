using Mediant.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

// IAuditLogged moved to Goldpath.Abstractions on 2026-09-09, to sit with the five entity
// markers it belongs beside. Source is unaffected — both are namespace Goldpath — so an
// adopter recompiles and nothing else. A TypeForwardedTo was tried first and withdrawn:
// PublicApiAnalyzers cannot reconcile a forwarded type, reporting RS0016 (not declared) and
// RS0017 (declared but not found) for the same symbol at once. On a pre-1.0 preview train
// the recompile is the cheaper honesty; the upgrade guide says so.
namespace Goldpath;

/// <summary>One audited property change. Added: old is null; Deleted: new is null.</summary>
public class GoldpathAuditLogEntry
{
    /// <summary>Row id.</summary>
    public long Id { get; set; }

    /// <summary>When the change was saved (UTC).</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Who made the change (<see cref="IUserContext"/>), or null for system flows.</summary>
    public string? User { get; set; }

    /// <summary>Owning tenant, or null in single-tenant deployments.</summary>
    public string? Tenant { get; set; }

    /// <summary>Correlation id of the originating flow (walks HTTP → command → entity rows).</summary>
    public string? CorrelationId { get; set; }

    /// <summary>CLR type name of the changed entity.</summary>
    public string EntityType { get; set; } = "";

    /// <summary>Primary key of the changed entity (composite keys joined with '|').</summary>
    public string EntityKey { get; set; } = "";

    /// <summary>Added | Modified | Deleted.</summary>
    public string Action { get; set; } = "";

    /// <summary>The changed property.</summary>
    public string PropertyName { get; set; } = "";

    /// <summary>Value before the change (null for Added, or in names-only mode).</summary>
    public string? OldValue { get; set; }

    /// <summary>Value after the change (null for Deleted, or in names-only mode).</summary>
    public string? NewValue { get; set; }
}

/// <summary>Model wiring for both audit levels.</summary>
public static class GoldpathAuditModelExtensions
{
    /// <summary>
    /// Maps the Goldpath entity-audit log AND Mediant's command-audit entity into the context —
    /// one call in <c>OnModelCreating</c> covers both levels of the story.
    /// </summary>
    public static ModelBuilder AddGoldpathAuditLog(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GoldpathAuditLogEntry>(entity =>
        {
            entity.ToTable("GoldpathAuditLog");
            entity.Property(e => e.EntityType).HasMaxLength(256);
            entity.Property(e => e.EntityKey).HasMaxLength(512);   // composite keys joined with '|' — explicit because it is indexed
            entity.Property(e => e.Action).HasMaxLength(16);
            // Before/after values copy ANY audited property — the app's own JSON and
            // long-text columns included. DOCUMENTS (#198): truncated audit evidence is
            // worse than none, because it reads as complete.
            entity.Property(e => e.OldValue).HasMaxLength(-1);
            entity.Property(e => e.NewValue).HasMaxLength(-1);
            entity.HasIndex(e => new { e.EntityType, e.EntityKey });
            entity.HasIndex(e => e.Timestamp);
        });

        return modelBuilder.ConfigureMediantAudit();
    }
}
