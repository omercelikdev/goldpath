namespace Goldpath;

/// <summary>
/// Identifies a tenant. The value is constrained to lowercase alphanumerics and dashes
/// (max 64 chars) so it can travel safely in HTTP headers, subdomains, and path prefixes.
/// </summary>
public readonly record struct TenantId
{
    /// <summary>Maximum allowed length of a tenant identifier.</summary>
    public const int MaxLength = 64;

    private TenantId(string value) => Value = value;

    /// <summary>The validated tenant identifier value.</summary>
    public string Value { get; }

    /// <summary>
    /// Creates a <see cref="TenantId"/> from <paramref name="value"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the value is null, empty, longer than <see cref="MaxLength"/>,
    /// or contains characters outside <c>[a-z0-9-]</c>.
    /// </exception>
    public static TenantId Create(string value)
        => TryCreate(value, out var id)
            ? id
            : throw new ArgumentException(
                $"'{value}' is not a valid tenant id: 1-{MaxLength} chars from [a-z0-9-].",
                nameof(value));

    /// <summary>
    /// Attempts to create a <see cref="TenantId"/> from <paramref name="value"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the value is valid; otherwise <see langword="false"/>.</returns>
    public static bool TryCreate(string? value, out TenantId tenantId)
    {
        tenantId = default;
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            {
                return false;
            }
        }

        tenantId = new TenantId(value);
        return true;
    }

    /// <summary>Returns the tenant identifier value.</summary>
    public override string ToString() => Value;
}
