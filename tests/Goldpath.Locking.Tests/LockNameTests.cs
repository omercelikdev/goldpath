using Xunit;

namespace Goldpath.Tests;

public sealed class LockNameTests
{
    private sealed class FixedTenant(string tenant) : ITenantContext
    {
        public TenantId? Current { get; } = TenantId.Create(tenant);
    }

    [Fact]
    public void Names_are_tenant_scoped_from_context_or_ambient()
    {
        Assert.Equal("goldpath:acme:lock:settlement", new GoldpathLockNames(new FixedTenant("acme")).For("settlement"));

        using (GoldpathTenant.Use("globex"))
        {
            Assert.Equal("goldpath:globex:lock:settlement", new GoldpathLockNames().For("settlement"));
        }
    }

    [Fact]
    public void No_tenant_fails_loudly_instead_of_colliding_silently()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new GoldpathLockNames().For("settlement"));
        Assert.Contains("GoldpathTenant.Use", ex.Message);   // the message teaches the fix
        Assert.Contains("Global", ex.Message);
    }

    [Fact]
    public void Global_is_explicit_and_tenant_free()
    {
        Assert.Equal("goldpath:global:lock:schema-migration", GoldpathLockNames.Global("schema-migration"));
        Assert.Throws<ArgumentException>(() => GoldpathLockNames.Global(""));
        Assert.Throws<ArgumentException>(() => new GoldpathLockNames(new FixedTenant("acme")).For(""));
    }

    [Fact]
    public void Two_tenants_never_share_a_lock_name()
    {
        string a, b;
        using (GoldpathTenant.Use("tenant-a"))
        {
            a = new GoldpathLockNames().For("nightly-report");
        }

        using (GoldpathTenant.Use("tenant-b"))
        {
            b = new GoldpathLockNames().For("nightly-report");
        }

        Assert.NotEqual(a, b);
    }
}
