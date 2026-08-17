using KynticAI.Scout.Infrastructure.Auth;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KynticAI.Scout.UnitTests;

public sealed class AuthenticationServiceTests
{
    [Theory]
    [InlineData("", "user@example.com", "password")]
    [InlineData("demo", "", "password")]
    [InlineData("demo", "user@example.com", "")]
    public async Task Login_MalformedRequiredInput_FailsGenerically_AndAudits(string tenantSlug, string email, string password)
    {
        var options = new DbContextOptionsBuilder<ScoutDbContext>()
            .UseInMemoryDatabase($"auth-invalid-{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ScoutDbContext(options);
        var authOptions = Options.Create(new AuthOptions
        {
            SigningKey = "unit-test-signing-key-that-is-long-enough-for-tests"
        });
        var timeProvider = TimeProvider.System;
        var service = new AuthenticationService(
            dbContext,
            new PasswordHashingService(),
            new JwtTokenService(authOptions, timeProvider),
            timeProvider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LoginAsync(tenantSlug, email, password, CancellationToken.None));

        Assert.Equal("Invalid tenant or credentials.", exception.Message);
        var audit = Assert.Single(await dbContext.AuditEvents.ToListAsync());
        Assert.Equal("auth.login.failed", audit.Action);
    }
}
