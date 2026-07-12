using CsCheck;
using Xunit;

namespace Goldpath.Tests;

/// <summary>
/// Property-based coverage of the TenantId grammar (foundation §8: unit level rides
/// property-based generation, not hand-picked examples).
/// </summary>
public class TenantIdPropertyTests
{
    private static readonly Gen<char> s_validChars =
        Gen.OneOf(Gen.Char['a', 'z'], Gen.Char['0', '9'], Gen.Const('-'));

    private static readonly Gen<string> s_validValues =
        s_validChars.Array[1, TenantId.MaxLength].Select(chars => new string(chars));

    [Fact]
    public void Every_valid_value_round_trips_and_compares_by_value()
        => s_validValues.Sample(value =>
        {
            Assert.True(TenantId.TryCreate(value, out var id));
            Assert.Equal(value, id.Value);
            Assert.Equal(value, id.ToString());
            Assert.Equal(TenantId.Create(value), id);          // value semantics
        });

    [Fact]
    public void Any_invalid_character_anywhere_rejects_the_whole_value()
        => Gen.Select(s_validValues, Gen.Char.Where(c => c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')), Gen.Int[0, 10])
            .Sample(input =>
            {
                var (valid, bad, position) = input;
                var index = Math.Min(position, valid.Length);
                var corrupted = valid.Insert(index, bad.ToString());
                Assert.False(corrupted.Length <= TenantId.MaxLength && TenantId.TryCreate(corrupted, out _));
            });

    [Fact]
    public void Length_boundary_is_exact()
    {
        Assert.True(TenantId.TryCreate(new string('a', TenantId.MaxLength), out _));
        Assert.False(TenantId.TryCreate(new string('a', TenantId.MaxLength + 1), out _));
        Assert.False(TenantId.TryCreate("", out _));
        Assert.False(TenantId.TryCreate(null, out _));
    }
}
