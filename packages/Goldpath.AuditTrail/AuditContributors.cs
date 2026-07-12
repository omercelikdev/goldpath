using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Goldpath;

/// <summary>Value-recording policy for entity audit rows (RFC decision D3).</summary>
public enum AuditValueMode
{
    /// <summary>Record old → new values (what makes audit useful; default).</summary>
    Full,

    /// <summary>
    /// Record property names only — the blunt global fallback. With the DataProtection
    /// module enabled, per-property masking of classified members is the recommended
    /// posture instead (values stay useful, PII stays out).
    /// </summary>
    NamesOnly,
}

/// <summary>Tuning surface — bound from <c>Goldpath:AuditTrail</c>.</summary>
public sealed class GoldpathAuditTrailOptions
{
    /// <summary>Value recording policy for entity-level rows.</summary>
    public AuditValueMode EntityValues { get; set; } = AuditValueMode.Full;
}

/// <summary>Stamps <see cref="IAuditedEntity"/> fields (who/when) on add and modify.</summary>
public sealed class AuditStampContributor : IEntitySaveContributor
{
    /// <inheritdoc />
    public void OnSaving(EntityEntry entry, GoldpathSaveContext context)
    {
        if (entry.Entity is not IAuditedEntity audited)
        {
            return;
        }

        var now = context.Clock.GetUtcNow();
        if (entry.State == EntityState.Added)
        {
            audited.CreatedAt = now;
            audited.CreatedBy = context.User;
        }
        else if (entry.State == EntityState.Modified)
        {
            audited.ModifiedAt = now;
            audited.ModifiedBy = context.User;
        }
    }
}

/// <summary>
/// Writes old → new change rows for <see cref="IAuditLogged"/> entities INTO THE SAME
/// DbContext — the audit row commits or dies with the change it describes (RFC decision D1).
/// </summary>
public sealed class AuditChangeLogContributor : IEntitySaveContributor
{
    private readonly GoldpathAuditTrailOptions _options;
    private readonly IGoldpathDataProtector? _dataProtector;

    /// <summary>
    /// Creates the contributor. The protector is the DataProtection module's masking seam —
    /// absent module, absent service, values recorded unmasked (compile-time composition).
    /// </summary>
    public AuditChangeLogContributor(GoldpathAuditTrailOptions options, IGoldpathDataProtector? dataProtector = null)
    {
        _options = options;
        _dataProtector = dataProtector;
    }

    /// <inheritdoc />
    public void OnSaving(EntityEntry entry, GoldpathSaveContext context)
    {
        if (entry.Entity is not IAuditLogged)
        {
            return;
        }

        var recordValues = _options.EntityValues == AuditValueMode.Full;
        var action = entry.State.ToString();
        var timestamp = context.Clock.GetUtcNow();
        var correlation = Activity.Current?.GetTagItem("goldpath.correlation_id") as string
            ?? Activity.Current?.TraceId.ToString();
        var key = string.Join("|", entry.Metadata.FindPrimaryKey()?.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? "") ?? []);

        var clrType = entry.Metadata.ClrType;
        foreach (var property in entry.Properties)
        {
            if (entry.State == EntityState.Modified && !property.IsModified)
            {
                continue;
            }

            var oldValue = recordValues && entry.State != EntityState.Added
                ? property.OriginalValue?.ToString()
                : null;
            var newValue = recordValues && entry.State != EntityState.Deleted
                ? property.CurrentValue?.ToString()
                : null;
            if (_dataProtector is not null)
            {
                oldValue = _dataProtector.Redact(clrType, property.Metadata.Name, oldValue);
                newValue = _dataProtector.Redact(clrType, property.Metadata.Name, newValue);
            }

            entry.Context.Add(new GoldpathAuditLogEntry
            {
                Timestamp = timestamp,
                User = context.User,
                Tenant = context.Tenant?.Value,
                CorrelationId = correlation,
                EntityType = clrType.Name,
                EntityKey = key,
                Action = action,
                PropertyName = property.Metadata.Name,
                OldValue = oldValue,
                NewValue = newValue,
            });
        }
    }
}
