using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Goldpath;

/// <summary>
/// Composition-safe global query filters. EF's <c>SetQueryFilter</c> REPLACES any existing
/// filter — two Goldpath modules (SoftDelete, MultiTenancy) both filtering the same entity would
/// silently erase each other. Every Goldpath module adds its filter through here instead:
/// filters are AND-combined regardless of registration order.
/// </summary>
public static class GoldpathQueryFilters
{
    /// <summary>Adds <paramref name="filter"/>, AND-combining with any existing filter.</summary>
    public static void AddFilter(IMutableEntityType entityType, LambdaExpression filter)
    {
        // EF 10 renamed the read side (named query filters); EF 8 (net8.0 target) only has
        // the classic API. Both write through SetQueryFilter with the combined lambda.
#if NET10_0_OR_GREATER
        var existing = entityType.GetDeclaredQueryFilters()
            .SingleOrDefault(f => f.Key is null)?.Expression;
#else
        var existing = entityType.GetQueryFilter();
#endif
        if (existing is null)
        {
            entityType.SetQueryFilter(filter);
            return;
        }

        var parameter = existing.Parameters[0];
        var rebound = new ReplaceParameterVisitor(filter.Parameters[0], parameter).Visit(filter.Body)!;
        entityType.SetQueryFilter(Expression.Lambda(Expression.AndAlso(existing.Body, rebound), parameter));
    }

    private sealed class ReplaceParameterVisitor(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
