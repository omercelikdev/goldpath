using Mediant.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Goldpath;

/// <summary>Marks an entity whose row-level changes are recorded as audit-log rows (old → new
/// per property, in the same transaction as the change). Stamp-only entities use
/// <see cref="IAuditedEntity"/>; this marker adds full change history (RFC decision D2).</summary>
public interface IAuditLogged;

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
            entity.Property(e => e.Action).HasMaxLength(16);
            entity.HasIndex(e => new { e.EntityType, e.EntityKey });
            entity.HasIndex(e => e.Timestamp);
        });

        return modelBuilder.ConfigureMediantAudit();
    }
}
