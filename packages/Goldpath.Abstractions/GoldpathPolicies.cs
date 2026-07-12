namespace Goldpath;

/// <summary>
/// Authorization policy names the asset agrees on across packages. The ADMIN surfaces
/// demand <see cref="Ops"/> OUT OF THE BOX (fail-closed — hardening H2); Goldpath.Auth
/// registers it from the manifest strategy, and the explicit opt-out on each mapper is
/// a VISIBLE decision, never a default.
/// </summary>
public static class GoldpathPolicies
{
    /// <summary>The ops-scoped policy every /goldpath/admin/* surface requires by default.</summary>
    public const string Ops = "goldpath-ops";
}
