namespace Goldpath;

/// <summary>Behavior when a request arrives while the same key is being processed (RFC 2.1).</summary>
public enum IdempotencyConflictBehavior
{
    /// <summary>Return 409 immediately — a fast signal to the client (HTTP default).</summary>
    Reject,

    /// <summary>Wait for the in-flight operation and then replay its result.</summary>
    Wait,
}

/// <summary>Payload fingerprint policy for reused keys.</summary>
public enum IdempotencyFingerprintMode
{
    /// <summary>Same key + different payload → 422 (Stripe-style semantics, default).</summary>
    Strict,

    /// <summary>Key only; payload differences replay silently.</summary>
    None,
}

/// <summary>
/// Tuning surface of the HTTP idempotency layer. Bound from configuration section
/// <c>Goldpath:Idempotency</c>, then the code callback applies. The command path is Mediant
/// <c>[Idempotent]</c> — same store, same semantics.
/// </summary>
public sealed class GoldpathIdempotencyOptions
{
    /// <summary>How long a stored response replays (manifest: <c>ttlHours</c>).</summary>
    public int TtlHours { get; set; } = 24;

    /// <summary>In-flight conflict behavior (manifest: <c>onConflict</c>).</summary>
    public IdempotencyConflictBehavior OnConflict { get; set; } = IdempotencyConflictBehavior.Reject;

    /// <summary>Payload fingerprint policy (manifest: <c>fingerprint</c>).</summary>
    public IdempotencyFingerprintMode Fingerprint { get; set; } = IdempotencyFingerprintMode.Strict;
}
