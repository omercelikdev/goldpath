using CsCheck;
using Xunit;

namespace Goldpath.Tests;

/// <summary>Property-based coverage of the key convention (foundation §8).</summary>
public class CacheKeyPropertyTests
{
    private static readonly Gen<string> s_tenants =
        Gen.Char.Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
            .Array[1, 20].Select(chars => new string(chars));

    private static readonly Gen<string> s_parts =
        Gen.Char.AlphaNumeric.Array[1, 30].Select(chars => new string(chars));

    [Fact]
    public void Distinct_tenants_never_collide_for_any_area_and_key()
        => Gen.Select(s_tenants, s_tenants, s_parts, s_parts)
            .Where(input => input.Item1 != input.Item2)
            .Sample(input => Assert.NotEqual(
                GoldpathCacheKeys.Compose(input.Item1, input.Item3, input.Item4),
                GoldpathCacheKeys.Compose(input.Item2, input.Item3, input.Item4)));

    [Fact]
    public void Keys_are_deterministic_and_carry_every_part()
        => Gen.Select(s_tenants, s_parts, s_parts).Sample(input =>
        {
            var (tenant, area, key) = input;
            var composed = GoldpathCacheKeys.Compose(tenant, area, key);
            Assert.Equal(composed, GoldpathCacheKeys.Compose(tenant, area, key));   // deterministic
            Assert.Equal($"goldpath:{tenant}:{area}:{key}", composed);              // exact shape
            Assert.StartsWith("goldpath:", composed, StringComparison.Ordinal);
        });

    [Fact]
    public void Null_or_empty_tenant_always_maps_to_the_placeholder()
        => Gen.Select(s_parts, s_parts).Sample(input =>
        {
            var (area, key) = input;
            Assert.Equal($"goldpath:_:{area}:{key}", GoldpathCacheKeys.Compose(null, area, key));
            Assert.Equal($"goldpath:_:{area}:{key}", GoldpathCacheKeys.Compose("", area, key));
        });
}
