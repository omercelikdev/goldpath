using Xunit;

namespace Goldpath.Tests;

public class CursorTests
{
    [Fact]
    public void Single_key_round_trips()
    {
        var cursor = GoldpathCursor.Encode(12345L);
        Assert.True(GoldpathCursor.TryDecode<long>(cursor, out var key));
        Assert.Equal(12345L, key);
    }

    [Fact]
    public void Composite_key_round_trips()
    {
        var createdAt = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);
        var cursor = GoldpathCursor.Encode(createdAt, Guid.Parse("4b4bd6b7-0000-0000-0000-000000000042"));

        Assert.True(GoldpathCursor.TryDecode<DateTimeOffset, Guid>(cursor, out var first, out var second));
        Assert.Equal(createdAt, first);
        Assert.Equal(Guid.Parse("4b4bd6b7-0000-0000-0000-000000000042"), second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64url!!")]
    [InlineData("eyJmb28iOiJiYXIifQ")] // valid base64url but a JSON object, not an array
    public void Tampered_or_invalid_cursor_fails_decode(string? cursor)
    {
        Assert.False(GoldpathCursor.TryDecode<long>(cursor, out _));
    }

    [Fact]
    public void Arity_mismatch_fails_decode()
    {
        var single = GoldpathCursor.Encode(1L);
        Assert.False(GoldpathCursor.TryDecode<long, long>(single, out _, out _));
    }

    [Fact]
    public void Cursor_is_url_safe()
    {
        // Values chosen to produce '+' and '/' in plain base64.
        var cursor = GoldpathCursor.Encode("??????>>>>>~~~", "///+++===???");
        Assert.DoesNotContain('+', cursor);
        Assert.DoesNotContain('/', cursor);
        Assert.DoesNotContain('=', cursor);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(50, 50)]
    [InlineData(9999, PageRequest.MaxSize)]
    public void Page_request_clamps_size(int requested, int expected)
    {
        Assert.Equal(expected, new PageRequest(Size: requested).ClampedSize);
    }
}
