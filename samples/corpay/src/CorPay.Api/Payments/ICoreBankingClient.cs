namespace CorPay.Api.Payments;

/// <summary>
/// The core-banking seam: execution is a PORT, not an HTTP call baked into a handler —
/// the dev implementation pays instantly; production plugs the bank's adapter here.
/// </summary>
public interface ICoreBankingClient
{
    /// <summary>Executes one instruction; returns the bank's receipt id. Throws on refusal.</summary>
    Task<string> ExecuteAsync(PaymentInstruction instruction, CancellationToken cancellationToken);
}

/// <summary>Development stand-in: every instruction executes, receipt is deterministic.</summary>
public sealed class DevCoreBankingClient : ICoreBankingClient
{
    public Task<string> ExecuteAsync(PaymentInstruction instruction, CancellationToken cancellationToken)
        => Task.FromResult($"DEV-{instruction.Reference}");
}
