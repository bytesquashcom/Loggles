using Loggles.Api.Configuration;
using Xunit;

namespace Loggles.Tests.Unit;

public sealed class AuthOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsEnabled_WhenApiKeyNullOrWhitespace_ReturnsFalse(string? key)
    {
        var options = new AuthOptions { ApiKey = key };
        Assert.False(options.IsEnabled);
    }

    [Theory]
    [InlineData("any-key")]
    [InlineData("x")]
    [InlineData("a very long key with spaces")]
    public void IsEnabled_WhenApiKeyHasValue_ReturnsTrue(string key)
    {
        var options = new AuthOptions { ApiKey = key };
        Assert.True(options.IsEnabled);
    }

    [Fact]
    public void IsEnabled_DefaultInstance_ReturnsFalse()
    {
        var options = new AuthOptions();
        Assert.False(options.IsEnabled);
    }
}
