namespace KynticAI.Scout.Infrastructure.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Issuer { get; set; } = "Scout";

    public string Audience { get; set; } = "KynticAI.Scout.Console";

    public string SigningKey { get; set; } = "scout-development-only-change-before-production";

    public int AccessTokenMinutes { get; set; } = 30;

    public bool RequireSecureSigningKey { get; set; } = true;

    public int MinimumSigningKeyLength { get; set; } = 32;

    public List<MachineClientOptions> MachineClients { get; set; } = [];
}

public sealed class MachineClientOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string TenantSlug { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Retained only for configuration compatibility with older Scout installs. Machine tokens
    /// are always issued with the dedicated api_client role; authority is defined by Scopes.
    /// </summary>
    public string Role { get; set; } = RoleNames.ApiClient;

    public List<string> Scopes { get; set; } = [];
}
