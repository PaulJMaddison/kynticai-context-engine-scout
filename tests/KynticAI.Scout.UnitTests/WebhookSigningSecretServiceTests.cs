using KynticAI.Scout.Infrastructure.Auth;

namespace KynticAI.Scout.UnitTests;

public sealed class WebhookSigningSecretServiceTests
{
    [Theory]
    [InlineData("2026-08-17T12:00:00Z", true)]
    [InlineData("2026-08-17T11:55:00Z", true)]
    [InlineData("2026-08-17T12:05:00Z", true)]
    [InlineData("2026-08-17T11:54:59Z", false)]
    [InlineData("2026-08-17T12:05:01Z", false)]
    [InlineData("not-a-time", false)]
    public void Freshness_UsesSuppliedClock_AndFiveMinuteBoundary(string timestamp, bool expected)
    {
        var now = DateTimeOffset.Parse("2026-08-17T12:00:00Z");

        Assert.Equal(expected, WebhookSigningSecretService.IsFresh(timestamp, now));
    }

    [Theory]
    [InlineData("", "2026-08-17T12:00:00Z", "{}", "sha256=abc")]
    [InlineData("api-key", "", "{}", "sha256=abc")]
    [InlineData("api-key", "2026-08-17T12:00:00Z", "{}", "")]
    public void LegacyHmac_BlankRequiredInput_FailsClosed(string apiKey, string timestamp, string body, string signature)
    {
        Assert.False(WebhookSigningSecretService.VerifyLegacyApiKeyHmac(apiKey, timestamp, body, signature));
    }
}
