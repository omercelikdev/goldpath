using Microsoft.Extensions.Compliance.Classification;

namespace Goldpath;

/// <summary>
/// The Goldpath data-classification taxonomy, built on the Microsoft compliance model so the
/// same annotation drives log redaction, audit masking, and any other compliance-aware sink.
/// </summary>
public static class GoldpathDataClassifications
{
    /// <summary>The taxonomy name under which Goldpath classifications are registered.</summary>
    public const string TaxonomyName = "Goldpath";

    /// <summary>GDPR-relevant identity data (national ids, names, contact details…).</summary>
    public static DataClassification Personal { get; } = new(TaxonomyName, nameof(Personal));

    /// <summary>Broader secrecy: financial, health, or otherwise confidential values.</summary>
    public static DataClassification Sensitive { get; } = new(TaxonomyName, nameof(Sensitive));
}

/// <summary>Classification levels usable from the DataProtection catalog API.</summary>
public enum GoldpathDataClass
{
    /// <summary>Maps to <see cref="GoldpathDataClassifications.Personal"/>.</summary>
    Personal,

    /// <summary>Maps to <see cref="GoldpathDataClassifications.Sensitive"/>.</summary>
    Sensitive,
}

/// <summary>
/// Marks a property or field as personal data (<see cref="GoldpathDataClassifications.Personal"/>).
/// Every compliance-aware sink — audit change rows, redacted logging — masks it.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class GoldpathPersonalDataAttribute : DataClassificationAttribute
{
    /// <summary>Creates the attribute.</summary>
    public GoldpathPersonalDataAttribute()
        : base(GoldpathDataClassifications.Personal)
    {
    }
}

/// <summary>
/// Marks a property or field as sensitive data (<see cref="GoldpathDataClassifications.Sensitive"/>).
/// Every compliance-aware sink — audit change rows, redacted logging — masks it.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class GoldpathSensitiveDataAttribute : DataClassificationAttribute
{
    /// <summary>Creates the attribute.</summary>
    public GoldpathSensitiveDataAttribute()
        : base(GoldpathDataClassifications.Sensitive)
    {
    }
}

/// <summary>
/// The classification-aware masking seam. Implemented by the DataProtection module; consumed
/// optionally by sinks (AuditTrail change rows). When the module is absent the service is
/// absent and sinks record values unmasked — compile-time composition intact.
/// </summary>
public interface IGoldpathDataProtector
{
    /// <summary>
    /// Property names declared through the code catalog — the eager, deterministic subset
    /// used to feed name-pattern sinks (e.g. Mediant's <c>SensitivePatterns</c>).
    /// Attribute-annotated members are discovered lazily and are not listed here.
    /// </summary>
    IReadOnlyCollection<string> CatalogedPropertyNames { get; }

    /// <summary>Whether the given property is classified (by attribute or catalog).</summary>
    bool IsClassified(Type declaringType, string propertyName);

    /// <summary>
    /// Redacts the value when the property is classified; returns it unchanged otherwise.
    /// <see langword="null"/> input is returned as-is.
    /// </summary>
    string? Redact(Type declaringType, string propertyName, string? value);
}
