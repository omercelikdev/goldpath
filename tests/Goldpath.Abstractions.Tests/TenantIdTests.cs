using Xunit;

namespace Goldpath.Tests;

public class TenantIdTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-bank-01")]
    [InlineData("a")]
    [InlineData("0")]
    public void TryCreate_accepts_valid_values(string value)
    {
        Assert.True(TenantId.TryCreate(value, out var id));
        Assert.Equal(value, id.Value);
        Assert.Equal(value, id.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Acme")]       // uppercase
    [InlineData("acme bank")]  // space
    [InlineData("acme_bank")]  // underscore
    [InlineData("acme.bank")]  // dot
    [InlineData(" açme")]      // non-ascii
    public void TryCreate_rejects_invalid_values(string? value)
    {
        Assert.False(TenantId.TryCreate(value, out _));
    }

    [Fact]
    public void TryCreate_rejects_values_longer_than_max()
    {
        var tooLong = new string('a', TenantId.MaxLength + 1);
        Assert.False(TenantId.TryCreate(tooLong, out _));
        Assert.True(TenantId.TryCreate(new string('a', TenantId.MaxLength), out _));
    }

    [Fact]
    public void Create_throws_for_invalid_value()
    {
        Assert.Throws<ArgumentException>(() => TenantId.Create("Not Valid"));
    }

    [Fact]
    public void Equality_is_by_value()
    {
        Assert.Equal(TenantId.Create("acme"), TenantId.Create("acme"));
        Assert.NotEqual(TenantId.Create("acme"), TenantId.Create("acme-2"));
    }
}
