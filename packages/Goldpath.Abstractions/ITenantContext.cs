namespace Goldpath;

/// <summary>
/// Ambient tenant of the current execution flow. Implemented by the MultiTenancy module
/// (resolved from header/subdomain/path per the manifest); consumers must treat
/// <see langword="null"/> as "no tenant" (single-tenant deployments).
/// </summary>
public interface ITenantContext
{
    /// <summary>The current tenant, or <see langword="null"/> when the deployment is single-tenant.</summary>
    TenantId? Current { get; }
}
