using KynticAI.Scout.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;

namespace KynticAI.Scout.Api.GraphQL;

internal static class GraphQlScopeGuard
{
    public static void RequireApiClientScope(IHttpContextAccessor httpContextAccessor, string requiredScope)
    {
        var user = httpContextAccessor.HttpContext?.User;
        // Machine identity is determined by the client_id claim, not by a role string. That keeps
        // scope enforcement intact even if older tokens/configuration carry an unexpected role.
        if (user?.FindFirst("client_id") is null)
        {
            return;
        }

        var scopes = user.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(ApiScopes.Normalize)
            .ToHashSet(StringComparer.Ordinal);

        if (!scopes.Contains(requiredScope))
        {
            throw new UnauthorizedAccessException($"API client scope '{requiredScope}' is required.");
        }
    }
}
