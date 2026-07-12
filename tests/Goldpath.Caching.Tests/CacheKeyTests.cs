using Xunit;

namespace Goldpath.Tests;

public sealed class CacheKeyTests
{
    private sealed class FixedTenant(string? tenant) : ITenantContext
    {
        public TenantId? Current { get; } = tenant is null ? null : TenantId.Create(tenant);
    }

    [Fact]
    public void Keys_are_tenant_scoped_with_a_placeholder_when_no_tenant_resolves()
    {
        Assert.Equal("goldpath:acme:rates:current", new GoldpathCacheKeys(new FixedTenant("acme")).For("rates", "current"));
        Assert.Equal("goldpath:_:rates:current", new GoldpathCacheKeys(new FixedTenant(null)).For("rates", "current"));
        Assert.Equal("goldpath:_:rates:current", new GoldpathCacheKeys().For("rates", "current"));
        Assert.Equal("goldpath:acme:rates:current", GoldpathCacheKeys.Compose("acme", "rates", "current"));
    }

    [Fact]
    public void Empty_area_or_key_fails_loudly()
    {
        Assert.Throws<ArgumentException>(() => GoldpathCacheKeys.Compose("t", "", "k"));
        Assert.Throws<ArgumentException>(() => GoldpathCacheKeys.Compose("t", "a", ""));
    }

    [Fact]
    public void Different_tenants_never_share_a_key()
    {
        var a = GoldpathCacheKeys.Compose("tenant-a", "rates", "current");
        var b = GoldpathCacheKeys.Compose("tenant-b", "rates", "current");
        Assert.NotEqual(a, b);
    }
}
