namespace CorPay.Api.Orders;

public partial class Order
{
    /// <summary>
    /// A LIVING example of classification: this value arrives masked (***) in audit change
    /// rows and redacted logs — classify once, every sink masks.
    /// </summary>
    [GoldpathPersonalData]
    public string? CustomerNationalId { get; set; }
}
