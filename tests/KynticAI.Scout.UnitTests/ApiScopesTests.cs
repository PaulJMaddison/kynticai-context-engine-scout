using KynticAI.Scout.Infrastructure.Auth;

namespace KynticAI.Scout.UnitTests;

public sealed class ApiScopesTests
{
    [Fact]
    public void Normalize_EmptyScopeSet_DoesNotGrantImplicitReadAccess()
    {
        var scopes = ApiScopes.Normalize([]);

        Assert.Empty(scopes);
    }

    [Fact]
    public void Normalize_MapsLegacyAliases_AndDeduplicates()
    {
        var scopes = ApiScopes.Normalize([" context.read ", "context:read", "events.write"]);

        Assert.Equal([ApiScopes.ContextRead, ApiScopes.EventsIngest], scopes);
    }
}
