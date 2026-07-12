namespace Goldpath;

/// <summary>Observability profile selected by the manifest (`observability.profile`).</summary>
public enum ObservabilityProfile
{
    /// <summary>Lowest overhead: 1% trace sampling.</summary>
    Minimal,

    /// <summary>Default: 10% parent-based trace sampling.</summary>
    Standard,

    /// <summary>Everything: always-on sampling.</summary>
    Full,
}

/// <summary>
/// Tuning surface of the Ring A floor. Pillars can be tuned, never disabled
/// (Ring A definition — see docs/rfc/goldpath-servicedefaults.md §1).
/// Bound from configuration section <c>Goldpath:ServiceDefaults</c>, then the code callback applies.
/// </summary>
public sealed class GoldpathServiceDefaultsOptions
{
    /// <summary>Telemetry tuning.</summary>
    public ObservabilityOptions Observability { get; } = new();

    /// <summary>Global concurrency guard tuning.</summary>
    public RateLimitingOptions RateLimiting { get; } = new();

    /// <summary>Correlation header behavior.</summary>
    public CorrelationOptions Correlation { get; } = new();

    /// <summary>Telemetry tuning (sampling is profile-driven unless overridden).</summary>
    public sealed class ObservabilityOptions
    {
        /// <summary>Profile from the manifest; Development always samples fully (RFC D4).</summary>
        public ObservabilityProfile Profile { get; set; } = ObservabilityProfile.Standard;

        /// <summary>Explicit trace sampling ratio (0..1); overrides the profile when set.</summary>
        public double? SamplingRatio { get; set; }
    }

    /// <summary>
    /// Collapse protection, not throttling (RFC D3): a generous process-wide concurrency
    /// cap that stays invisible until an incident.
    /// </summary>
    public sealed class RateLimitingOptions
    {
        /// <summary>Maximum concurrently processed requests.</summary>
        public int ConcurrencyLimit { get; set; } = 1000;

        /// <summary>Requests queued when the limit is reached; beyond this, 429.</summary>
        public int QueueLimit { get; set; } = 100;
    }

    /// <summary>Correlation header behavior (<see cref="GoldpathHeaders.CorrelationId"/>).</summary>
    public sealed class CorrelationOptions
    {
        /// <summary>
        /// Whether an inbound correlation id is honored. When <see langword="false"/>
        /// (or the header is absent) a new id is generated from the current trace id.
        /// </summary>
        public bool AcceptInbound { get; set; } = true;
    }
}
