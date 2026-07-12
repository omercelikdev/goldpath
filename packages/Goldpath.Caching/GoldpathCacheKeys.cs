namespace Goldpath;

/// <summary>
/// The tenant-scoped cache key convention: <c>goldpath:{tenant}:{area}:{key}</c> (RFC decision D3).
/// Tenant scoping is baked in before the MultiTenancy module lands — retrofitting a key
/// format means invalidating every key in production, and cross-tenant cache bleed is a
/// security bug, not a performance bug. <c>_</c> stands in when no tenant is resolved.
/// </summary>
public sealed class GoldpathCacheKeys
{
    private readonly ITenantContext? _tenant;

    /// <summary>Creates the helper; the tenant context is optional (single-tenant apps).</summary>
    public GoldpathCacheKeys(ITenantContext? tenant = null) => _tenant = tenant;

    /// <summary>Builds a key for the current tenant.</summary>
    public string For(string area, string key) => Compose(_tenant?.Current?.Value, area, key);

    /// <summary>Builds a key for an explicit tenant (background/message flows).</summary>
    public static string Compose(string? tenant, string area, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(area);
        ArgumentException.ThrowIfNullOrEmpty(key);
        return $"goldpath:{(string.IsNullOrEmpty(tenant) ? "_" : tenant)}:{area}:{key}";
    }
}
