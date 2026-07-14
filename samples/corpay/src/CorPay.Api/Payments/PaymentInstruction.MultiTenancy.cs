namespace CorPay.Api.Payments;

public partial class PaymentInstruction : IMultiTenant
{
    /// <summary>Owning corporate — stamped by the MultiTenancy contributor, never by hand (GP0902).</summary>
    public TenantId TenantId { get; set; }
}
