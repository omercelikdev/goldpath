namespace CorPay.Api.Payments;

/// <summary>The finance card's control values — constants, visible, in one place.</summary>
public static class PaymentPolicy
{
    /// <summary>At or above this amount a SECOND person must approve before money moves.</summary>
    public const decimal FourEyesThreshold = 100_000m;
}
