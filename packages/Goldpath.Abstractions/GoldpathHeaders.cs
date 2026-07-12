namespace Goldpath;

/// <summary>
/// Canonical HTTP header names used across Goldpath seams. Middleware, message filters,
/// and clients must reference these constants instead of string literals.
/// </summary>
public static class GoldpathHeaders
{
    /// <summary>Tenant identifier header (header-strategy multi-tenancy).</summary>
    public const string TenantId = "X-Goldpath-Tenant";

    /// <summary>Idempotency key header (IETF draft-standard name).</summary>
    public const string IdempotencyKey = "Idempotency-Key";

    /// <summary>API key header (the ApiKey auth strategy).</summary>
    public const string ApiKey = "X-Goldpath-Api-Key";

    /// <summary>
    /// Explicit correlation id header for systems that cannot consume W3C
    /// <c>traceparent</c>; tracing itself always uses OpenTelemetry.
    /// </summary>
    public const string CorrelationId = "X-Correlation-Id";
}
