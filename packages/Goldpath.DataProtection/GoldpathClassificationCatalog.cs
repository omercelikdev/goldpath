using System.Linq.Expressions;

namespace Goldpath;

/// <summary>
/// Code-based classification catalog (RFC decision D4) — for scaffolded/legacy entities whose
/// source must not be touched. Type-safe: refactoring renames survive; config-file mappings
/// would drift silently. Catalog entries win over attribute annotations on the same member.
/// </summary>
public sealed class GoldpathClassificationCatalog
{
    private readonly Dictionary<(Type Type, string Property), GoldpathDataClass> _entries = [];

    internal IReadOnlyDictionary<(Type Type, string Property), GoldpathDataClass> Entries => _entries;

    /// <summary>Classifies a property of <typeparamref name="T"/> without annotating it.</summary>
    public GoldpathClassificationCatalog Classify<T>(Expression<Func<T, object?>> property, GoldpathDataClass dataClass)
    {
        var body = property.Body is UnaryExpression { NodeType: ExpressionType.Convert } unary
            ? unary.Operand
            : property.Body;
        if (body is not MemberExpression member)
        {
            throw new ArgumentException("The expression must select a property or field, e.g. x => x.TaxNumber.", nameof(property));
        }

        _entries[(typeof(T), member.Member.Name)] = dataClass;
        return this;
    }
}
