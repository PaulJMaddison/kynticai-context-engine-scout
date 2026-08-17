using KynticAI.Scout.Infrastructure.Auth;

namespace KynticAI.Scout.UnitTests;

public sealed class PasswordHashingServiceTests
{
    [Fact]
    public void HashPassword_ProducesVerifiableHash()
    {
        var service = new PasswordHashingService();

        var hash = service.HashPassword("DemoAdmin123!");

        Assert.NotEqual("DemoAdmin123!", hash);
        Assert.True(service.VerifyPassword("DemoAdmin123!", hash));
        Assert.False(service.VerifyPassword("WrongPassword123!", hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-hash")]
    [InlineData("pbkdf2-sha256$600000$%%%$%%%")]
    [InlineData("pbkdf2-sha256$0$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$2000001$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void VerifyPassword_ReturnsFalse_ForMalformedOrUnsafeStoredHash(string storedHash)
    {
        var service = new PasswordHashingService();

        var matches = service.VerifyPassword("DemoAdmin123!", storedHash);

        Assert.False(matches);
    }

    [Fact]
    public void VerifyPassword_ReturnsFalse_WhenDecodedSaltOrHashHasUnexpectedSize()
    {
        var service = new PasswordHashingService();
        var shortSalt = Convert.ToBase64String(new byte[8]);
        var shortHash = Convert.ToBase64String(new byte[16]);

        Assert.False(service.VerifyPassword("DemoAdmin123!", $"pbkdf2-sha256$600000${shortSalt}${Convert.ToBase64String(new byte[32])}"));
        Assert.False(service.VerifyPassword("DemoAdmin123!", $"pbkdf2-sha256$600000${Convert.ToBase64String(new byte[16])}${shortHash}"));
    }
}
