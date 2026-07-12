namespace Goldpath;

/// <summary>
/// The tenant-scoped lock name convention: <c>goldpath:{tenant}:lock:{name}</c> (RFC decision D3).
/// Two tenants' "nightly-report" locks must never collide — a cross-tenant lock collision is
/// a correctness bug. <see cref="For"/> FAILS LOUDLY without an ambient tenant (fail-closed,
/// like the write guard): background flows either pin one with <c>GoldpathTenant.Use(...)</c> or
/// say <see cref="Global"/> explicitly.
/// </summary>
public sealed class GoldpathLockNames
{
    private readonly ITenantContext? _tenant;

    /// <summary>Creates the helper; the tenant context is optional (single-tenant apps use <see cref="Global"/>).</summary>
    public GoldpathLockNames(ITenantContext? tenant = null) => _tenant = tenant;

    /// <summary>Builds a lock name for the current tenant; throws when none is resolved.</summary>
    public string For(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var tenant = _tenant?.Current ?? GoldpathAmbientTenant.Current
            ?? throw new InvalidOperationException(
                $"Lock '{name}' needs a tenant — resolve one via the request, pin one with "
                + "GoldpathTenant.Use(tenant), or declare the lock infrastructure-wide with GoldpathLockNames.Global(name).");
        return $"goldpath:{tenant.Value}:lock:{name}";
    }

    /// <summary>Builds a deliberately tenant-free name for infrastructure-wide locks (greppable).</summary>
    public static string Global(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return $"goldpath:global:lock:{name}";
    }
}
