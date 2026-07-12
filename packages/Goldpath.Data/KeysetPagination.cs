using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Goldpath;

/// <summary>Sort direction of a keyset pagination key.</summary>
public enum SortDirection
{
    /// <summary>Ascending order.</summary>
    Ascending,

    /// <summary>Descending order.</summary>
    Descending,
}

/// <summary>
/// Thrown when an inbound cursor cannot be decoded (tampered, truncated, or from a different
/// endpoint). API layers must surface this as HTTP 400 — the golden-path template wires that
/// mapping; it is never a 500.
/// </summary>
public sealed class GoldpathInvalidCursorException : Exception
{
    /// <summary>Creates the exception.</summary>
    public GoldpathInvalidCursorException()
        : base("The pagination cursor is invalid. Cursors are opaque; reuse only values returned in 'nextCursor'.")
    {
    }
}

/// <summary>
/// The keyset (cursor) pagination executor — the data-side half of the ApiDefaults wire
/// contract. Applies its own ordering from the key selectors (a mismatched order-by is the
/// classic skipped-rows bug), fetches size+1 to detect the last page, and never translates
/// to OFFSET. 1–2 keys, per-key direction (RFC decision D5).
/// </summary>
public static class GoldpathKeysetPaginationExtensions
{
    /// <summary>
    /// Executes single-key keyset pagination. The key must be unique (e.g. the id).
    /// Parameters are required by design: only the longest overload may carry optionals (RS0026/27).
    /// </summary>
    public static async Task<Page<T>> ToPageAsync<T, TKey>(
        this IQueryable<T> query,
        PageRequest request,
        Expression<Func<T, TKey>> key,
        SortDirection direction,
        CancellationToken cancellationToken)
        where TKey : IComparable<TKey>
    {
        var size = request.ClampedSize;
        var ordered = direction == SortDirection.Ascending ? query.OrderBy(key) : query.OrderByDescending(key);

        if (request.Cursor is not null)
        {
            if (!GoldpathCursor.TryDecode<TKey>(request.Cursor, out var after) || after is null)
            {
                throw new GoldpathInvalidCursorException();
            }

            ordered = (IOrderedQueryable<T>)ordered.Where(CompareTo(key, after, direction));
        }

        var items = await ordered.Take(size + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return BuildPage(items, size, last => GoldpathCursor.Encode(key.Compile()(last)));
    }

    /// <summary>
    /// Executes composite keyset pagination over the canonical (key, tiebreaker) pair —
    /// e.g. (createdAt, id). The pair must be unique together. As the longest overload this
    /// is the one carrying the optional parameters (RS0026/27).
    /// </summary>
    public static async Task<Page<T>> ToPageAsync<T, TKey1, TKey2>(
        this IQueryable<T> query,
        PageRequest request,
        Expression<Func<T, TKey1>> key1,
        Expression<Func<T, TKey2>> key2,
        SortDirection direction1 = SortDirection.Ascending,
        SortDirection direction2 = SortDirection.Ascending,
        CancellationToken cancellationToken = default)
        where TKey1 : IComparable<TKey1>
        where TKey2 : IComparable<TKey2>
    {
        var size = request.ClampedSize;
        var ordered = direction1 == SortDirection.Ascending ? query.OrderBy(key1) : query.OrderByDescending(key1);
        ordered = direction2 == SortDirection.Ascending ? ordered.ThenBy(key2) : ordered.ThenByDescending(key2);

        if (request.Cursor is not null)
        {
            if (!GoldpathCursor.TryDecode<TKey1, TKey2>(request.Cursor, out var after1, out var after2)
                || after1 is null || after2 is null)
            {
                throw new GoldpathInvalidCursorException();
            }

            // (k1 ≻ v1) OR (k1 = v1 AND k2 ≻ v2) — provider-neutral row-value comparison.
            var parameter = Expression.Parameter(typeof(T), "e");
            var beyondFirst = CompareBody(key1, after1, direction1, parameter);
            var firstEqual = Expression.Equal(
                CompareCall(key1, after1, parameter),
                Expression.Constant(0));
            var beyondSecond = CompareBody(key2, after2, direction2, parameter);
            var predicate = Expression.Lambda<Func<T, bool>>(
                Expression.OrElse(beyondFirst, Expression.AndAlso(firstEqual, beyondSecond)),
                parameter);

            ordered = (IOrderedQueryable<T>)ordered.Where(predicate);
        }

        var compiled1 = key1.Compile();
        var compiled2 = key2.Compile();
        var items = await ordered.Take(size + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return BuildPage(items, size, last => GoldpathCursor.Encode(compiled1(last), compiled2(last)));
    }

    private static Page<T> BuildPage<T>(List<T> fetched, int size, Func<T, string> encodeCursor)
    {
        var hasMore = fetched.Count > size;
        if (hasMore)
        {
            fetched.RemoveAt(size);
        }

        return new Page<T>(fetched, hasMore ? encodeCursor(fetched[^1]) : null, size);
    }

    private static Expression<Func<T, bool>> CompareTo<T, TKey>(
        Expression<Func<T, TKey>> key, TKey after, SortDirection direction)
        where TKey : IComparable<TKey>
    {
        var parameter = Expression.Parameter(typeof(T), "e");
        return Expression.Lambda<Func<T, bool>>(CompareBody(key, after, direction, parameter), parameter);
    }

    // key.CompareTo(after) > 0 (asc) / < 0 (desc) — CompareTo translates on all relational providers.
    private static BinaryExpression CompareBody<T, TKey>(
        Expression<Func<T, TKey>> key, TKey after, SortDirection direction, ParameterExpression parameter)
        where TKey : IComparable<TKey>
    {
        var comparison = CompareCall(key, after, parameter);
        return direction == SortDirection.Ascending
            ? Expression.GreaterThan(comparison, Expression.Constant(0))
            : Expression.LessThan(comparison, Expression.Constant(0));
    }

    private static MethodCallExpression CompareCall<T, TKey>(
        Expression<Func<T, TKey>> key, TKey after, ParameterExpression parameter)
        where TKey : IComparable<TKey>
    {
        var body = new ParameterReplacer(key.Parameters[0], parameter).Visit(key.Body);
        var compareTo = typeof(TKey).GetMethod(nameof(IComparable<TKey>.CompareTo), [typeof(TKey)])!;
        return Expression.Call(body, compareTo, Expression.Constant(after, typeof(TKey)));
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
