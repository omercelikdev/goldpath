using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Goldpath;

/// <summary>
/// Ambient values handed to save contributors: the clock (BCL <see cref="TimeProvider"/> —
/// no custom IClock, ADR-0003) and the current tenant (<see langword="null"/> in
/// single-tenant deployments).
/// </summary>
public sealed record GoldpathSaveContext(TimeProvider Clock, TenantId? Tenant, string? User = null);

/// <summary>
/// The data-seam plug-in point for Ring B modules (AuditTrail, SoftDelete, MultiTenancy…).
/// Application code does not implement this — modules do; compile-time composition holds:
/// a disabled module registers no contributor.
/// </summary>
public interface IEntitySaveContributor
{
    /// <summary>
    /// Execution order across modules (lower runs first). Default 0; state-converting
    /// contributors (e.g. SoftDelete at -100) run before observers like audit/stamps.
    /// </summary>
    int Order => 0;

    /// <summary>Invoked for every Added/Modified/Deleted entry before changes are saved.</summary>
    void OnSaving(EntityEntry entry, GoldpathSaveContext context);
}

/// <summary>
/// The single Goldpath save interceptor: runs all registered <see cref="IEntitySaveContributor"/>s
/// over tracked entries on every save (sync and async).
/// </summary>
public sealed class GoldpathSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IReadOnlyList<IEntitySaveContributor> _contributors;
    private readonly GoldpathSaveContext _context;

    /// <summary>Creates the interceptor over the registered contributors.</summary>
    public GoldpathSaveChangesInterceptor(IEnumerable<IEntitySaveContributor> contributors, GoldpathSaveContext context)
    {
        _contributors = contributors.OrderBy(static c => c.Order).ToArray();
        _context = context;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Run(eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Run(eventData);
        return ValueTask.FromResult(result);
    }

    private void Run(DbContextEventData eventData)
    {
        if (_contributors.Count == 0 || eventData.Context is not { } dbContext)
        {
            return;
        }

        // Snapshot: contributors may ADD entities (e.g. audit-log rows) while we iterate.
        foreach (var entry in dbContext.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            foreach (var contributor in _contributors)
            {
                contributor.OnSaving(entry, _context);
            }
        }
    }
}
