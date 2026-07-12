namespace Goldpath;

/// <summary>
/// The flow-scoped ambient tenant — the single carrier every seam writes and reads
/// (<see cref="System.Diagnostics.Activity.Current"/>-style). Set by module entry points:
/// the MultiTenancy HTTP middleware, the Messaging consume filter, and
/// <c>GoldpathTenant.Use(...)</c> for background flows. Read by <see cref="ITenantContext"/>
/// implementations and EF query filters. Application code should read
/// <see cref="ITenantContext"/>, not this.
/// </summary>
public static class GoldpathAmbientTenant
{
    private static readonly AsyncLocal<TenantId?> s_current = new();

    /// <summary>The ambient tenant of the current async flow, or <see langword="null"/>.</summary>
    public static TenantId? Current
    {
        get => s_current.Value;
        set => s_current.Value = value;
    }
}
