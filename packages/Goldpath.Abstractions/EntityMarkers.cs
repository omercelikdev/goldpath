namespace Goldpath;

/// <summary>
/// Marks an entity whose creation/modification metadata is maintained automatically
/// by the data-path interceptor of the AuditTrail module. Setters are for infrastructure;
/// application code must never write them.
/// </summary>
public interface IAuditedEntity
{
    /// <summary>When the entity was created.</summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>Who created the entity (user or system identity).</summary>
    string? CreatedBy { get; set; }

    /// <summary>When the entity was last modified, or <see langword="null"/> if never.</summary>
    DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>Who last modified the entity, or <see langword="null"/> if never modified.</summary>
    string? ModifiedBy { get; set; }
}

/// <summary>
/// Marks an entity that is soft-deleted: deletes become updates and a global query filter
/// hides deleted rows. Maintained by the SoftDelete module's interceptor; setters are for
/// infrastructure only.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>Whether the entity is deleted.</summary>
    bool IsDeleted { get; set; }

    /// <summary>When the entity was deleted, or <see langword="null"/> while it is live.</summary>
    DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Who deleted the entity, or <see langword="null"/> while it is live.</summary>
    string? DeletedBy { get; set; }
}

/// <summary>
/// Marks an entity that belongs to a tenant. The MultiTenancy module's interceptor stamps
/// the value on write and applies the tenant query filter on read.
/// </summary>
public interface IMultiTenant
{
    /// <summary>The owning tenant.</summary>
    TenantId TenantId { get; set; }
}
