using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GoldpathTemplate.Application.Orders;

/// <summary>
/// The application's view of persistence (clean-architecture seam): handlers depend on
/// THIS, the Infrastructure project implements it with the real DbContext. DbSets stay
/// EF's own types on purpose — the seam separates PROJECTS, it does not re-abstract EF
/// (ADR-0003: what Microsoft provides is composed, never wrapped).
/// </summary>
public interface IOrdersDbContext
{
    /// <summary>The orders aggregate root set.</summary>
    DbSet<Order> Orders { get; }

    /// <summary>The database facade — transactions for the outbox pattern.</summary>
    DatabaseFacade Database { get; }

    /// <summary>Persists pending changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
