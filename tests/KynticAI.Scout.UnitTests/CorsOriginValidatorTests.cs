using KynticAI.Scout.Infrastructure.Configuration;

namespace KynticAI.Scout.UnitTests;

public sealed class CorsOriginValidatorTests
{
    [Theory]
    [InlineData("https://app.example.com")]
    [InlineData("https://app.example.com:8443")]
    [InlineData("http://localhost:5173")]
    [InlineData("http://127.0.0.1:5173")]
    [InlineData("http://[::1]:5173")]
    public void HostedMode_AcceptsExactHttpsOrLoopbackOrigins(string origin)
    {
        var valid = CorsOriginValidator.TryValidate(origin, hostedMode: true, out var error);

        Assert.True(valid, error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("https://user@example.com")]
    [InlineData("https://example.com/path")]
    [InlineData("https://example.com?query=1")]
    [InlineData("https://example.com#fragment")]
    [InlineData("http://example.com")]
    [InlineData("http://evil-localhost.example")]
    [InlineData("http://127.0.0.1.example.com")]
    public void HostedMode_RejectsMalformedOrInsecureOrigins(string origin)
    {
        var valid = CorsOriginValidator.TryValidate(origin, hostedMode: true, out var error);

        Assert.False(valid);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void DevelopmentMode_AllowsExactHttpRemoteOrigin()
    {
        var valid = CorsOriginValidator.TryValidate("http://dev.example.com:5173", hostedMode: false, out var error);

        Assert.True(valid, error);
    }
}
