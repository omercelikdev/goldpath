using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Goldpath;

/// <summary>
/// Explicit tenant scopes (RFC decisions D3/D5 — SoftDelete's proven AsyncLocal pattern):
/// <see cref="Bypass"/> widens READS for admin/reporting flows (the query filter turns off;
/// the write guard stays ON); <see cref="Use(TenantId)"/> pins the ambient tenant so
/// background jobs and seeding write AS an explicit tenant.
/// </summary>
public static class GoldpathTenant
{
    private static readonly AsyncLocal<bool> s_bypassed = new();

    /// <summary>Whether the tenant query filter is currently bypassed on this flow.</summary>
    public static bool IsBypassed => s_bypassed.Value;

    /// <summary>Turns the tenant query filter off until the scope is disposed. Writes stay guarded.</summary>
    public static IDisposable Bypass() => new BypassScope();

    /// <summary>Pins the ambient tenant for the current flow (background jobs, seeding).</summary>
    public static IDisposable Use(TenantId tenant) => new UseScope(tenant);

    /// <summary>Pins the ambient tenant, validating the raw value (<see cref="TenantId.Create"/> rules).</summary>
    public static IDisposable Use(string tenant) => new UseScope(TenantId.Create(tenant));

    private sealed class BypassScope : IDisposable
    {
        private readonly bool _previous;

        public BypassScope()
        {
            _previous = s_bypassed.Value;
            s_bypassed.Value = true;
        }

        public void Dispose() => s_bypassed.Value = _previous;
    }

    private sealed class UseScope : IDisposable
    {
        private readonly TenantId? _previous;

        public UseScope(TenantId tenant)
        {
            _previous = GoldpathAmbientTenant.Current;
            GoldpathAmbientTenant.Current = tenant;
        }

        public void Dispose() => GoldpathAmbientTenant.Current = _previous;
    }
}

/// <summary>Reads the ambient tenant — the scoped <see cref="ITenantContext"/> of HTTP flows.</summary>
public sealed class AmbientTenantContext : ITenantContext
{
    /// <inheritdoc />
    public TenantId? Current => GoldpathAmbientTenant.Current;
}

/// <summary>
/// A save touched another tenant's row — a security event, not a bug to log (RFC D3).
/// The transaction aborts; the trip is counted on <c>goldpath_tenant_write_guard_trips_total</c>.
/// </summary>
public sealed class GoldpathCrossTenantWriteException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public GoldpathCrossTenantWriteException(string message)
        : base(message)
    {
    }
}

/// <summary>Module meters — flow into the Ring A OTel pipeline.</summary>
internal static class GoldpathMultiTenancyMetrics
{
    private static readonly Meter s_meter = new("Goldpath.MultiTenancy");

    public static readonly Counter<long> Unresolved =
        s_meter.CreateCounter<long>("goldpath_tenant_unresolved_total");

    public static readonly Counter<long> GuardTrips =
        s_meter.CreateCounter<long>("goldpath_tenant_write_guard_trips_total");
}

/// <summary>
/// Stamps the ambient tenant on Added <see cref="IMultiTenant"/> entities and guards every
/// write: touching another tenant's row (or writing with no ambient tenant at all) throws.
/// Runs at order −200 — before SoftDelete's conversion and the audit observers, so a
/// violation aborts the save before anything else contributes. The guard reads the ambient
/// tenant AT SAVE TIME, so correctness never depends on when the DbContext was resolved.
/// </summary>
public sealed class TenantStampContributor : IEntitySaveContributor
{
    /// <inheritdoc />
    public int Order => -200;

    /// <inheritdoc />
    public void OnSaving(EntityEntry entry, GoldpathSaveContext context)
    {
        if (entry.Entity is not IMultiTenant multiTenant)
        {
            return;
        }

        var ambient = GoldpathAmbientTenant.Current;
        var entityName = entry.Metadata.ClrType.Name;

        if (entry.State == EntityState.Added)
        {
            if (multiTenant.TenantId == default)
            {
                multiTenant.TenantId = ambient ?? Trip(
                    $"Adding '{entityName}' requires an ambient tenant — resolve one via the "
                    + "request, or wrap background flows in GoldpathTenant.Use(tenant).");
            }
            else if (ambient is null || multiTenant.TenantId != ambient.Value)
            {
                Trip($"Adding '{entityName}' with explicit tenant '{multiTenant.TenantId.Value}' "
                    + $"does not match the ambient tenant '{ambient?.Value ?? "<none>"}'. "
                    + "Cross-tenant writes require GoldpathTenant.Use(tenant).");
            }
        }
        else
        {
            var original = (TenantId)entry.Property(nameof(IMultiTenant.TenantId)).OriginalValue!;
            if (ambient is null || original != ambient.Value)
            {
                Trip($"Writing '{entityName}' owned by tenant '{original.Value}' from ambient "
                    + $"tenant '{ambient?.Value ?? "<none>"}'. Bypass() only widens reads; "
                    + "cross-tenant writes require GoldpathTenant.Use(tenant).");
            }
        }
    }

    private static TenantId Trip(string message)
    {
        GoldpathMultiTenancyMetrics.GuardTrips.Add(1);
        throw new GoldpathCrossTenantWriteException(message);
    }
}
