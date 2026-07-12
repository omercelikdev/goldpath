namespace GoldpathTemplate.Api.Orders;

public partial class Order : ISoftDeletable
{
    /// <summary>Maintained by the SoftDelete contributor; deletes become stamped updates.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>When the entity was deleted, or null while it is live.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Who deleted the entity, or null while it is live.</summary>
    public string? DeletedBy { get; set; }
}
