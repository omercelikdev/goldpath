namespace CorPay.Api.Payments;

public partial class PaymentInstruction
{
    /// <summary>Classified once — audit change rows and redacted logs mask it everywhere.</summary>
    [GoldpathPersonalData]
    public string DebtorIban { get; set; } = "";

    /// <summary>Classified once — audit change rows and redacted logs mask it everywhere.</summary>
    [GoldpathPersonalData]
    public string CreditorIban { get; set; } = "";
}
