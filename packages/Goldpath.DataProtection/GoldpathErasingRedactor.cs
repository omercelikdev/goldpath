using Microsoft.Extensions.Compliance.Redaction;

namespace Goldpath;

/// <summary>
/// Default redactor: replaces the value with the fixed <c>***</c> token. A visible token
/// (unlike the built-in empty-string erasure) keeps "the value changed" readable in audit
/// rows and logs while revealing nothing — including the length.
/// </summary>
public sealed class GoldpathErasingRedactor : Redactor
{
    /// <summary>The token classified values are replaced with.</summary>
    public const string Token = "***";

    /// <inheritdoc />
    public override int GetRedactedLength(ReadOnlySpan<char> input) => Token.Length;

    /// <inheritdoc />
    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        Token.AsSpan().CopyTo(destination);
        return Token.Length;
    }
}
