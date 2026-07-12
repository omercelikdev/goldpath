using CsCheck;
using Xunit;

namespace Goldpath.Tests;

/// <summary>
/// Property-based coverage of the cursor codec: edge cases are generated, not imagined
/// (foundation §8.2 — the Mockifyr fuzzing lesson applied to the golden path).
/// </summary>
public class CursorPropertyTests
{
    [Fact]
    public void Long_cursors_round_trip_for_any_value()
        => Gen.Long.Sample(value =>
        {
            Assert.True(GoldpathCursor.TryDecode<long>(GoldpathCursor.Encode(value), out var decoded));
            Assert.Equal(value, decoded);
        });

    [Fact]
    public void Unicode_string_cursors_round_trip()
        => Gen.String.Sample(value =>
        {
            Assert.True(GoldpathCursor.TryDecode<string>(GoldpathCursor.Encode(value), out var decoded));
            Assert.Equal(value, decoded);
        });

    [Fact]
    public void Composite_datetimeoffset_guid_cursors_round_trip()
        => Gen.Select(Gen.DateTimeOffset, Gen.Guid).Sample(pair =>
        {
            var cursor = GoldpathCursor.Encode(pair.Item1, pair.Item2);
            Assert.True(GoldpathCursor.TryDecode<DateTimeOffset, Guid>(cursor, out var first, out var second));
            Assert.Equal(pair.Item1, first);
            Assert.Equal(pair.Item2, second);
        });

    [Fact]
    public void Arbitrary_garbage_never_throws_only_fails_decode()
        => Gen.String.Sample(garbage =>
        {
            // Whatever the input, TryDecode must be total: false, never an exception.
            GoldpathCursor.TryDecode<long>(garbage, out _);
            GoldpathCursor.TryDecode<DateTimeOffset, Guid>(garbage, out _, out _);
        });

    [Fact]
    public void Single_key_cursor_never_decodes_as_composite_and_vice_versa()
        => Gen.Long.Sample(value =>
        {
            Assert.False(GoldpathCursor.TryDecode<long, long>(GoldpathCursor.Encode(value), out _, out _));
            Assert.False(GoldpathCursor.TryDecode<long>(GoldpathCursor.Encode(value, value), out _));
        });
}
