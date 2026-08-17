using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Domain.Saas;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KynticAI.Scout.Infrastructure.Auth;

public sealed class MachineClientAuthenticationService(
    IOptions<AuthOptions> options,
    JwtTokenService jwtTokenService,
    ScoutDbContext dbContext,
    PasswordHashingService passwordHashingService)
{
    private const string InvalidCredentialsMessage = "Invalid client credentials.";

    public async Task<MachineTokenResult> AuthenticateAsync(
        string clientId,
        string clientSecret,
        string? requestedScope,
        CancellationToken cancellationToken)
    {
        var normalizedClientId = clientId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedClientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(InvalidCredentialsMessage);
        }

        var persistedClient = await dbContext.ApiClients
            .AsTracking()
            .Include(x => x.Tenant)
            .Include(x => x.Workspace)
            .FirstOrDefaultAsync(candidate => candidate.ClientId == normalizedClientId, cancellationToken);
        if (persistedClient is not null)
        {
            if (persistedClient.Status != ApiClientStatus.Active
                || !passwordHashingService.VerifyPassword(clientSecret, persistedClient.SecretHash))
            {
                throw new InvalidOperationException(InvalidCredentialsMessage);
            }

            var configuredScopes = DeserializeScopes(persistedClient.ScopesJson);
            var persistedGrantedScopes = ResolveGrantedScopes(configuredScopes, requestedScope);
            var persistedMachineClient = new MachineClientOptions
            {
                ClientId = persistedClient.ClientId,
                ClientSecret = string.Empty,
                TenantSlug = persistedClient.Tenant.Slug,
                DisplayName = persistedClient.DisplayName,
                Role = RoleNames.ApiClient,
                Scopes = persistedGrantedScopes.ToList()
            };
            var persistedToken = jwtTokenService.CreateMachineToken(
                persistedClient.Tenant,
                persistedMachineClient,
                persistedGrantedScopes,
                persistedClient.Workspace);
            var issuedAtUtc = DateTime.UtcNow;
            persistedClient.MarkUsed(issuedAtUtc);
            dbContext.AuditEvents.Add(CreateTokenIssuedAudit(
                persistedClient.TenantId,
                persistedClient.ClientId,
                persistedClient.Id,
                persistedClient.Workspace,
                persistedGrantedScopes,
                persistedToken.ExpiresAtUtc,
                issuedAtUtc));
            await dbContext.SaveChangesAsync(cancellationToken);

            return new MachineTokenResult(
                persistedToken.AccessToken,
                persistedToken.ExpiresAtUtc,
                persistedGrantedScopes);
        }

        var authOptions = options.Value;
        var machineClient = authOptions.MachineClients.FirstOrDefault(candidate =>
            string.Equals(candidate.ClientId, normalizedClientId, StringComparison.Ordinal));

        if (machineClient is null || !SecretsMatch(machineClient.ClientSecret, clientSecret))
        {
            throw new InvalidOperationException(InvalidCredentialsMessage);
        }

        var tenantSlug = machineClient.TenantSlug.Trim().ToLowerInvariant();
        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Slug == tenantSlug, cancellationToken);
        if (tenant is null)
        {
            throw new InvalidOperationException(InvalidCredentialsMessage);
        }

        var grantedScopes = ResolveGrantedScopes(machineClient.Scopes, requestedScope);
        var token = jwtTokenService.CreateMachineToken(tenant, machineClient, grantedScopes);
        dbContext.AuditEvents.Add(CreateTokenIssuedAudit(
            tenant.Id,
            machineClient.ClientId,
            null,
            null,
            grantedScopes,
            token.ExpiresAtUtc,
            DateTime.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);

        return new MachineTokenResult(
            token.AccessToken,
            token.ExpiresAtUtc,
            grantedScopes);
    }

    private static AuditEvent CreateTokenIssuedAudit(
        Guid tenantId,
        string clientId,
        Guid? apiClientId,
        Workspace? workspace,
        IReadOnlyList<string> grantedScopes,
        DateTime expiresAtUtc,
        DateTime utcNow)
        => AuditEvent.Create(
            tenantId,
            $"client:{clientId}",
            "auth.token.issued",
            nameof(ApiClient),
            apiClientId?.ToString("D") ?? clientId,
            Guid.NewGuid().ToString("N"),
            JsonSerializer.Serialize(new
            {
                clientId,
                workspaceSlug = workspace?.Slug,
                scopes = grantedScopes
            }),
            null,
            JsonSerializer.Serialize(new { expiresAtUtc }),
            utcNow);

    private static IReadOnlyList<string> DeserializeScopes(string scopesJson)
    {
        if (string.IsNullOrWhiteSpace(scopesJson))
        {
            return [];
        }

        try
        {
            return ApiScopes.Normalize(JsonSerializer.Deserialize<IReadOnlyList<string>>(scopesJson, JsonSerializerOptions.Web) ?? []);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ResolveGrantedScopes(IReadOnlyList<string> configuredScopes, string? requestedScope)
    {
        var normalizedConfigured = ApiScopes.Normalize(configuredScopes).ToList();

        if (normalizedConfigured.Count == 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(requestedScope))
        {
            return normalizedConfigured;
        }

        var requested = requestedScope
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ApiScopes.Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (requested.Any(scope => !normalizedConfigured.Contains(scope, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(InvalidCredentialsMessage);
        }

        return requested;
    }

    private static bool SecretsMatch(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected ?? string.Empty);
        var actualBytes = Encoding.UTF8.GetBytes(actual ?? string.Empty);

        if (expectedBytes.Length != actualBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}

public sealed record MachineTokenResult(
    string AccessToken,
    DateTime ExpiresAtUtc,
    IReadOnlyList<string> GrantedScopes);