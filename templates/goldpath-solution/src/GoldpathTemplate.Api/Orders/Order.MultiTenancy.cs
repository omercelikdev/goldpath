namespace GoldpathTemplate.Api.Orders;

public partial class Order : IMultiTenant
{
    /// <summary>Owning tenant — stamped by the MultiTenancy contributor, never by hand (GP0902).</summary>
    public TenantId TenantId { get; set; }
}
