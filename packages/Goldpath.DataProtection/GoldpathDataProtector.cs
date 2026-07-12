using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;

namespace Goldpath;

/// <summary>
/// Resolves a member's classification from the catalog (wins ties) and from annotations:
/// the Goldpath attributes, any <see cref="DataClassificationAttribute"/>-derived attribute, and
/// Mediant's <c>[SensitiveData]</c> matched by name (no hard dependency). Redaction is
/// delegated to the composed <see cref="IRedactorProvider"/>.
/// </summary>
public sealed class GoldpathDataProtector : IGoldpathDataProtector
{
    private readonly ConcurrentDictionary<(Type Type, string Property), GoldpathDataClass?> _cache = new();
    private readonly IReadOnlyDictionary<(Type Type, string Property), GoldpathDataClass> _catalog;
    private readonly IRedactorProvider _redactors;

    /// <summary>Creates the protector over the configured catalog and redactor provider.</summary>
    public GoldpathDataProtector(GoldpathDataProtectionOptions options, IRedactorProvider redactors)
    {
        _catalog = options.CatalogInstance.Entries.ToDictionary(e => e.Key, e => e.Value);
        _redactors = redactors;
        CatalogedPropertyNames = _catalog.Keys.Select(k => k.Property).Distinct().ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> CatalogedPropertyNames { get; }

    /// <summary>The effective classification of a member, or <see langword="null"/>.</summary>
    public GoldpathDataClass? Classify(Type declaringType, string propertyName)
        => _cache.GetOrAdd((declaringType, propertyName), Compute);

    /// <inheritdoc />
    public bool IsClassified(Type declaringType, string propertyName)
        => Classify(declaringType, propertyName) is not null;

    /// <inheritdoc />
    public string? Redact(Type declaringType, string propertyName, string? value)
    {
        if (value is null || Classify(declaringType, propertyName) is not { } dataClass)
        {
            return value;
        }

        var classification = dataClass == GoldpathDataClass.Personal
            ? GoldpathDataClassifications.Personal
            : GoldpathDataClassifications.Sensitive;
        return _redactors.GetRedactor(classification).Redact(value);
    }

    private GoldpathDataClass? Compute((Type Type, string Property) key)
    {
        if (_catalog.TryGetValue(key, out var cataloged))
        {
            return cataloged;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        MemberInfo? member = key.Type.GetProperty(key.Property, flags)
            ?? (MemberInfo?)key.Type.GetField(key.Property, flags);
        if (member is null)
        {
            return null;
        }

        foreach (var attribute in member.GetCustomAttributes())
        {
            if (attribute is DataClassificationAttribute classified)
            {
                return classified.Classification.Equals(GoldpathDataClassifications.Personal)
                    ? GoldpathDataClass.Personal
                    : GoldpathDataClass.Sensitive;
            }

            // Mediant's [SensitiveData] — recognized by name, no hard dependency (ADR-0003).
            var type = attribute.GetType();
            if (type.Name == "SensitiveDataAttribute"
                && type.Namespace?.StartsWith("Mediant", StringComparison.Ordinal) is true)
            {
                return GoldpathDataClass.Sensitive;
            }
        }

        return null;
    }
}
