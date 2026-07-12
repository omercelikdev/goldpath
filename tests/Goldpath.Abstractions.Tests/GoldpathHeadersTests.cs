using Xunit;

namespace Goldpath.Tests;

public class GoldpathHeadersTests
{
    // Header names are wire contracts: changing one is a breaking change and must fail a test.
    [Fact]
    public void Header_names_are_locked()
    {
        Assert.Equal("X-Goldpath-Tenant", GoldpathHeaders.TenantId);
        Assert.Equal("Idempotency-Key", GoldpathHeaders.IdempotencyKey);
        Assert.Equal("X-Correlation-Id", GoldpathHeaders.CorrelationId);
    }
}
