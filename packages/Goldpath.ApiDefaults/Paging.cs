using System.Text;
using System.Text.Json;

namespace Goldpath;

/// <summary>
/// Cursor-pagination request. The wire contract of the Goldpath pagination primitive;
/// the <c>IQueryable</c> keyset executor ships with Goldpath.Data.
/// </summary>
/// <param name="Cursor">Opaque cursor from a previous <see cref="Page{T}.NextCursor"/>; <see langword="null"/> for the first page.</param>
/// <param name="Size">Requested page size; effective value is <see cref="ClampedSize"/>.</param>
public sealed record PageRequest(string? Cursor = null, int Size = PageRequest.DefaultSize)
{
    /// <summary>Default page size when unspecified.</summary>
    public const int DefaultSize = 50;

    /// <summary>Hard upper bound for a single page.</summary>
    public const int MaxSize = 200;

    /// <summary>The size actually applied: <see cref="Size"/> clamped to 1..<see cref="MaxSize"/>.</summary>
    public int ClampedSize => Math.Clamp(Size, 1, MaxSize);
}

/// <summary>
/// Cursor-pagination response. Deliberately carries no total count — counting large tables
/// is the offset trap reborn (RFC decision D2); consumers needing totals get an explicit
/// aggregate endpoint.
/// </summary>
/// <param name="Items">The page items.</param>
/// <param name="NextCursor">Cursor of the next page; <see langword="null"/> means the end.</param>
/// <param name="Size">The applied page size.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, int Size);

/// <summary>
/// Opaque cursor codec: base64url-encoded JSON of the keyset values. Server-controlled —
/// consumers must treat cursors as opaque strings; a tampered cursor fails
/// <c>TryDecode</c> and should surface as HTTP 400.
/// </summary>
public static class GoldpathCursor
{
    private static readonly JsonSerializerOptions s_json = JsonSerializerOptions.Default;

    /// <summary>Encodes a single-key cursor.</summary>
    public static string Encode<T>(T key)
        => ToBase64Url(JsonSerializer.SerializeToUtf8Bytes(new object?[] { key }, s_json));

    /// <summary>Encodes a composite (two-key) cursor — the typical keyset pair, e.g. (createdAt, id).</summary>
    public static string Encode<T1, T2>(T1 first, T2 second)
        => ToBase64Url(JsonSerializer.SerializeToUtf8Bytes(new object?[] { first, second }, s_json));

    /// <summary>Attempts to decode a single-key cursor.</summary>
    public static bool TryDecode<T>(string? cursor, out T? key)
    {
        key = default;
        if (TryReadValues(cursor, expectedCount: 1) is not { } values)
        {
            return false;
        }

        return TryConvert(values[0], out key);
    }

    /// <summary>Attempts to decode a composite (two-key) cursor.</summary>
    public static bool TryDecode<T1, T2>(string? cursor, out T1? first, out T2? second)
    {
        first = default;
        second = default;
        if (TryReadValues(cursor, expectedCount: 2) is not { } values)
        {
            return false;
        }

        return TryConvert(values[0], out first) && TryConvert(values[1], out second);
    }

    private static JsonElement[]? TryReadValues(string? cursor, int expectedCount)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(FromBase64Url(cursor));
            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() != expectedCount)
            {
                return null;
            }

            var values = new JsonElement[expectedCount];
            var i = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                values[i++] = element.Clone();
            }

            return values;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryConvert<T>(JsonElement element, out T? value)
    {
        try
        {
            value = element.Deserialize<T>(s_json);
            return value is not null;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var builder = new StringBuilder(value).Replace('-', '+').Replace('_', '/');
        builder.Append(new string('=', (4 - (value.Length % 4)) % 4));
        return Convert.FromBase64String(builder.ToString());
    }
}
