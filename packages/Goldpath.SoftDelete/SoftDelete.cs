using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>
/// The explicit hard-delete escape hatch (RFC decision D1): inside a
/// <see cref="Suppress"/> scope, deletes of <see cref="ISoftDeletable"/> entities really
/// delete. Visible in code review, greppable, async-safe — built for
/// right-to-erasure flows, not for convenience.
/// </summary>
public static class GoldpathSoftDelete
{
    private static readonly AsyncLocal<bool> s_suppressed = new();

    /// <summary>Whether soft-delete conversion is currently suppressed on this flow.</summary>
    public static bool IsSuppressed => s_suppressed.Value;

    /// <summary>Suppresses soft-delete conversion until the returned scope is disposed.</summary>
    public static IDisposable Suppress() => new SuppressScope();

    private sealed class SuppressScope : IDisposable
    {
        private readonly bool _previous;

        public SuppressScope()
        {
            _previous = s_suppressed.Value;
            s_suppressed.Value = true;
        }

        public void Dispose() => s_suppressed.Value = _previous;
    }
}

/// <summary>
/// Converts deletes of <see cref="ISoftDeletable"/> entities into stamped updates.
/// Runs at order −100, before audit/stamp contributors — they observe the converted truth:
/// a soft delete audits as the <c>IsDeleted false→true</c> change, in the same transaction.
/// </summary>
public sealed class SoftDeleteContributor : IEntitySaveContributor
{
    /// <inheritdoc />
    public int Order => -100;

    /// <inheritdoc />
    public void OnSaving(EntityEntry entry, GoldpathSaveContext context)
    {
        if (entry.State != EntityState.Deleted
            || entry.Entity is not ISoftDeletable softDeletable
            || GoldpathSoftDelete.IsSuppressed)
        {
            return;
        }

        // Unchanged first, then touch ONLY the soft-delete fields — the resulting UPDATE
        // (and the audit change rows) cover exactly these three properties, nothing else.
        entry.State = EntityState.Unchanged;
        softDeletable.IsDeleted = true;
        softDeletable.DeletedAt = context.Clock.GetUtcNow();
        softDeletable.DeletedBy = context.User;
        entry.Property(nameof(ISoftDeletable.IsDeleted)).IsModified = true;
        entry.Property(nameof(ISoftDeletable.DeletedAt)).IsModified = true;
        entry.Property(nameof(ISoftDeletable.DeletedBy)).IsModified = true;
    }
}

/// <summary>Registration and model wiring for soft delete.</summary>
public static class GoldpathSoftDeleteExtensions
{
    /// <summary>Registers the soft-delete contributor on the Data seam.</summary>
    public static TBuilder AddGoldpathSoftDelete<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddScoped<IEntitySaveContributor, SoftDeleteContributor>();
        return builder;
    }

    /// <summary>
    /// Adds the <c>!IsDeleted</c> global query filter to every <see cref="ISoftDeletable"/>
    /// entity — call from <c>OnModelCreating</c> (the template generates the call; forgetting
    /// it is analyzer rule GP0601).
    /// </summary>
    public static ModelBuilder ApplyGoldpathSoftDelete(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var filter = Expression.Lambda(
                Expression.Not(Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted))),
                parameter);
            GoldpathQueryFilters.AddFilter(entityType, filter);   // AND-combines with other modules' filters
        }

        return modelBuilder;
    }
}
