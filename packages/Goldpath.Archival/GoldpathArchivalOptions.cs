using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Goldpath;

/// <summary>Module options: the declared archives and row retentions (bound + fluent).</summary>
public sealed class GoldpathArchivalOptions
{
    internal List<GoldpathArchiveDefinition> ArchiveList { get; } = [];
    internal List<GoldpathRowRetentionDefinition> RowRetentionList { get; } = [];

    /// <summary>Discovery/purge batch size per chunk.</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>The declared graph archives.</summary>
    public IReadOnlyList<GoldpathArchiveDefinition> Archives => ArchiveList;

    /// <summary>The declared row retentions.</summary>
    public IReadOnlyList<GoldpathRowRetentionDefinition> RowRetentions => RowRetentionList;

    /// <summary>Declares a GRAPH archive for an aggregate root (archival RFC D2/D5).</summary>
    public GoldpathArchivalOptions AddArchive<TRoot>(Action<GoldpathArchiveBuilder<TRoot>> configure)
        where TRoot : class
    {
        var builder = new GoldpathArchiveBuilder<TRoot>();
        configure(builder);
        ArchiveList.Add(builder.Build());
        return this;
    }

    /// <summary>Declares a ROW retention (bulk age-out with an explicit safety guard — D5).</summary>
    public GoldpathArchivalOptions AddRowRetention<TRow>(Action<GoldpathRowRetentionBuilder<TRow>> configure)
        where TRow : class
    {
        var builder = new GoldpathRowRetentionBuilder<TRow>();
        configure(builder);
        RowRetentionList.Add(builder.Build());
        return this;
    }
}

/// <summary>One aggregate loaded for archiving.</summary>
public sealed record GoldpathArchiveCandidate(string Key, object Root, DateTimeOffset DueAt, string? Tenant);

/// <summary>
/// A declared graph archive with its execution baked as typed closures at registration
/// time — the engine stays generic-free, the queries stay EF-translatable and compile-checked.
/// </summary>
public sealed class GoldpathArchiveDefinition
{
    internal GoldpathArchiveDefinition(string name, Type rootType)
    {
        Name = name;
        RootType = rootType;
    }

    /// <summary>Definition name (defaults to the root type name; the chain is per name).</summary>
    public string Name { get; }

    /// <summary>The aggregate root CLR type.</summary>
    public Type RootType { get; }

    /// <summary>Hot period after the due event before the aggregate archives.</summary>
    public TimeSpan ArchiveAfter { get; internal set; } = TimeSpan.Zero;

    /// <summary>Archive-entry retention; null = keep forever (purge job skips).</summary>
    public TimeSpan? RetainFor { get; internal set; }

    /// <summary>Whether the archive run REMOVES the hot rows (move, not copy).</summary>
    public bool DeleteHotRows { get; internal set; }

    /// <summary>Include paths captured for diagnostics/tests.</summary>
    public IReadOnlyList<string> GraphPaths { get; internal set; } = [];

    internal Func<DbContext, DateTimeOffset, int, int, CancellationToken, Task<List<string>>> DiscoverDueKeysAsync { get; set; } = null!;
    internal Func<DbContext, string, CancellationToken, Task<GoldpathArchiveCandidate?>> LoadAsync { get; set; } = null!;
    internal Func<DbContext, string, CancellationToken, Task> RemoveHotAsync { get; set; } = null!;
    internal Func<DbContext, DateTimeOffset, CancellationToken, Task<int>> CountDueAsync { get; set; } = null!;
}

/// <summary>Fluent shape for one graph archive.</summary>
public sealed class GoldpathArchiveBuilder<TRoot>
    where TRoot : class
{
    private readonly List<string> _paths = [];
    private LambdaExpression? _keySelector;
    private Func<string, Expression<Func<TRoot, bool>>>? _keyEquality;
    private Expression<Func<TRoot, bool>>? _due;
    private Expression<Func<TRoot, DateTimeOffset>>? _dueAt;
    private Func<TRoot, string?>? _tenant;
    private TimeSpan _archiveAfter = TimeSpan.Zero;
    private TimeSpan? _retainFor;
    private bool _deleteHot;
    private string? _name;

    /// <summary>Overrides the definition name (defaults to the root type name).</summary>
    public GoldpathArchiveBuilder<TRoot> Named(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>The owned graph as navigation selectors (compile-checked include paths).</summary>
    public GoldpathArchiveBuilder<TRoot> Graph(params Expression<Func<TRoot, object?>>[] navigations)
    {
        foreach (var navigation in navigations)
        {
            _paths.Add(ToPath(navigation));
        }

        return this;
    }

    /// <summary>Deep include paths in EF's dotted-string form (for nested graphs).</summary>
    public GoldpathArchiveBuilder<TRoot> GraphPath(params string[] paths)
    {
        _paths.AddRange(paths);
        return this;
    }

    /// <summary>The aggregate key (stringified invariantly into the archive index).</summary>
    public GoldpathArchiveBuilder<TRoot> Key<TKey>(Expression<Func<TRoot, TKey>> key)
    {
        _keySelector = key;
        // Bake a TRANSLATABLE equality here, while TKey is still in hand — retrieval and
        // hot-row removal must be indexed lookups, never in-memory scans.
        _keyEquality = keyString =>
        {
            var typed = typeof(TKey) == typeof(Guid)
                ? (object)Guid.Parse(keyString)
                : Convert.ChangeType(keyString, typeof(TKey), CultureInfo.InvariantCulture);
            return Expression.Lambda<Func<TRoot, bool>>(
                Expression.Equal(key.Body, Expression.Constant(typed, typeof(TKey))),
                key.Parameters);
        };
        return this;
    }

    /// <summary>The lifecycle event: WHEN the aggregate is done, and its event time.</summary>
    public GoldpathArchiveBuilder<TRoot> DueWhen(Expression<Func<TRoot, bool>> due, Expression<Func<TRoot, DateTimeOffset>> dueAt)
    {
        _due = due;
        _dueAt = dueAt;
        return this;
    }

    /// <summary>Hot period after the due event (how long the aggregate stays queryable hot).</summary>
    public GoldpathArchiveBuilder<TRoot> ArchiveAfter(TimeSpan period)
    {
        _archiveAfter = period;
        return this;
    }

    /// <summary>Archive-entry retention in years (then the purge job removes — unless held).</summary>
    public GoldpathArchiveBuilder<TRoot> RetainFor(int years)
    {
        _retainFor = TimeSpan.FromDays(365.25 * years);
        return this;
    }

    /// <summary>Move, not copy: the archive run deletes the hot rows after a committed entry.</summary>
    public GoldpathArchiveBuilder<TRoot> DeleteHotRowsAfterArchive()
    {
        _deleteHot = true;
        return this;
    }

    /// <summary>Tenant selector (entries and retrieval are tenant-scoped when supplied).</summary>
    public GoldpathArchiveBuilder<TRoot> Tenant(Func<TRoot, string?> tenant)
    {
        _tenant = tenant;
        return this;
    }

    internal GoldpathArchiveDefinition Build()
    {
        if (_keySelector is null || _keyEquality is null || _due is null || _dueAt is null)
        {
            throw new InvalidOperationException(
                $"Archive of {typeof(TRoot).Name} needs Key(...) and DueWhen(...) — the lifecycle must be modeled, never guessed (GP1403).");
        }

        var definition = new GoldpathArchiveDefinition(_name ?? typeof(TRoot).Name, typeof(TRoot))
        {
            ArchiveAfter = _archiveAfter,
            RetainFor = _retainFor,
            DeleteHotRows = _deleteHot,
            GraphPaths = _paths.ToList(),
        };

        var due = _due;
        var dueAtExpr = _dueAt;
        var dueAtCompiled = _dueAt.Compile();
        var tenant = _tenant;
        var paths = definition.GraphPaths;
        var keyExpr = _keySelector;
        var keyEquality = _keyEquality;

        definition.CountDueAsync = (db, cutoff, ct) =>
            db.Set<TRoot>().Where(due).Where(Le(dueAtExpr, cutoff)).CountAsync(ct);

        definition.DiscoverDueKeysAsync = async (db, cutoff, skip, take, ct) =>
        {
            var keys = await db.Set<TRoot>()
                .Where(due)
                .Where(Le(dueAtExpr, cutoff))
                .OrderBy(WrapAsObject(keyExpr))
                .Select(WrapAsObject(keyExpr))
                .Skip(skip)
                .Take(take)
                .ToListAsync(ct);
            return keys.Select(k => Convert.ToString(k, CultureInfo.InvariantCulture) ?? "").ToList();
        };

        definition.LoadAsync = async (db, key, ct) =>
        {
            IQueryable<TRoot> query = db.Set<TRoot>();
            foreach (var path in paths)
            {
                query = query.Include(path);
            }

            var root = await query.Where(keyEquality(key)).FirstOrDefaultAsync(ct);   // indexed lookup
            return root is null ? null : new GoldpathArchiveCandidate(key, root, dueAtCompiled(root), tenant?.Invoke(root));
        };

        definition.RemoveHotAsync = async (db, key, ct) =>
        {
            var root = await db.Set<TRoot>().Where(keyEquality(key)).FirstOrDefaultAsync(ct);
            if (root is not null)
            {
                db.Remove(root);   // cascades follow the model (owned graph deletes with the root)
            }
        };

        return definition;
    }

    private static Expression<Func<TRoot, object?>> WrapAsObject(LambdaExpression selector)
    {
        var body = selector.Body.Type.IsValueType
            ? Expression.Convert(selector.Body, typeof(object))
            : selector.Body;
        return Expression.Lambda<Func<TRoot, object?>>(body, selector.Parameters);
    }

    private static Expression<Func<TRoot, bool>> Le(Expression<Func<TRoot, DateTimeOffset>> dueAt, DateTimeOffset cutoff)
        => Expression.Lambda<Func<TRoot, bool>>(
            Expression.LessThanOrEqual(dueAt.Body, Expression.Constant(cutoff)),
            dueAt.Parameters);

    private static string ToPath(Expression<Func<TRoot, object?>> navigation)
    {
        var body = navigation.Body is UnaryExpression { NodeType: ExpressionType.Convert } unary
            ? unary.Operand
            : navigation.Body;
        var parts = new Stack<string>();
        while (body is MemberExpression member)
        {
            parts.Push(member.Member.Name);
            body = member.Expression!;
        }

        if (parts.Count == 0)
        {
            throw new InvalidOperationException("Graph selectors must be navigation member chains (c => c.Decisions).");
        }

        return string.Join('.', parts);
    }
}

/// <summary>A declared row retention with its baked purge closures.</summary>
public sealed class GoldpathRowRetentionDefinition
{
    internal GoldpathRowRetentionDefinition(string name, Type rowType)
    {
        Name = name;
        RowType = rowType;
    }

    /// <summary>Definition name (the row type name).</summary>
    public string Name { get; }

    /// <summary>The detail row CLR type.</summary>
    public Type RowType { get; }

    /// <summary>Age after which rows purge.</summary>
    public TimeSpan After { get; internal set; }

    /// <summary>Whether an explicit safety guard was declared (GP1402 watches this).</summary>
    public bool HasGuard { get; internal set; }

    internal Func<DbContext, DateTimeOffset, CancellationToken, Task<int>> CountDueCore { get; set; } = null!;
    internal Func<DbContext, DateTimeOffset, int, CancellationToken, Task<int>> PurgeBatchCore { get; set; } = null!;

    /// <summary>Rows currently purgeable (guard + age applied).</summary>
    public Task<int> CountDueAsync(DbContext db, DateTimeOffset now, CancellationToken cancellationToken)
        => CountDueCore(db, now, cancellationToken);

    /// <summary>Purges ONE bounded, ordered batch; returns how many rows left.</summary>
    public Task<int> PurgeBatchAsync(DbContext db, DateTimeOffset now, int batchSize, CancellationToken cancellationToken)
        => PurgeBatchCore(db, now, batchSize, cancellationToken);
}

/// <summary>Fluent shape for one row retention.</summary>
public sealed class GoldpathRowRetentionBuilder<TRow>
    where TRow : class
{
    private Expression<Func<TRow, DateTimeOffset>>? _age;
    private TimeSpan _after;
    private Expression<Func<TRow, bool>>? _guard;

    /// <summary>Rows older than this (by the given timestamp) become purgeable.</summary>
    public GoldpathRowRetentionBuilder<TRow> After(TimeSpan period, Expression<Func<TRow, DateTimeOffset>> age)
    {
        _after = period;
        _age = age;
        return this;
    }

    /// <summary>The "safe to purge" predicate — age is rarely the only truth (GP1402).</summary>
    public GoldpathRowRetentionBuilder<TRow> Where(Expression<Func<TRow, bool>> guard)
    {
        _guard = guard;
        return this;
    }

    internal GoldpathRowRetentionDefinition Build()
    {
        if (_age is null)
        {
            throw new InvalidOperationException($"Row retention of {typeof(TRow).Name} needs After(period, ageSelector).");
        }

        var definition = new GoldpathRowRetentionDefinition(typeof(TRow).Name, typeof(TRow))
        {
            After = _after,
            HasGuard = _guard is not null,
        };

        var age = _age;
        var guard = _guard;

        IQueryable<TRow> Due(DbContext db, DateTimeOffset now)
        {
            var query = db.Set<TRow>().AsQueryable();
            if (guard is not null)
            {
                query = query.Where(guard);
            }

            var cutoff = now - definition.After;
            var predicate = Expression.Lambda<Func<TRow, bool>>(
                Expression.LessThanOrEqual(age.Body, Expression.Constant(cutoff)),
                age.Parameters);
            return query.Where(predicate);
        }

        definition.CountDueCore = (db, now, ct) => Due(db, now).CountAsync(ct);
        definition.PurgeBatchCore = async (db, now, batch, ct) =>
        {
            // Ordered, bounded deletes: never one giant statement (the MDM lesson — a
            // 100k-row DELETE is a lock storm; 200-row batches are boring, which is the point).
            var page = await Due(db, now).OrderBy(age).Take(batch).ToListAsync(ct);
            if (page.Count == 0)
            {
                return 0;
            }

            db.RemoveRange(page);
            await db.SaveChangesAsync(ct);
            return page.Count;
        };

        return definition;
    }
}
