using CsCheck;
using Xunit;

namespace Goldpath.Tests;

/// <summary>Property-based coverage of the lock-name convention (foundation §8).</summary>
public class LockNamePropertyTests
{
    private static readonly Gen<string> s_tenants =
        Gen.Char.Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
            .Array[1, 20].Select(chars => new string(chars));

    private static readonly Gen<string> s_names =
        Gen.Char.AlphaNumeric.Array[1, 30].Select(chars => new string(chars));

    [Fact]
    public void Distinct_tenants_never_contend_for_any_lock_name()
        => Gen.Select(s_tenants, s_tenants, s_names)
            .Where(input => input.Item1 != input.Item2)
            .Sample(input =>
            {
                var (first, second, name) = input;
                string a, b;
                using (GoldpathTenant.Use(first))
                {
                    a = new GoldpathLockNames().For(name);
                }

                using (GoldpathTenant.Use(second))
                {
                    b = new GoldpathLockNames().For(name);
                }

                Assert.NotEqual(a, b);
            });

    [Fact]
    public void Global_and_tenant_scoped_namespaces_never_overlap()
        => Gen.Select(s_tenants, s_names).Sample(input =>
        {
            var (tenant, name) = input;
            using (GoldpathTenant.Use(tenant))
            {
                Assert.NotEqual(GoldpathLockNames.Global(name), new GoldpathLockNames().For(name));
            }
        });
}
