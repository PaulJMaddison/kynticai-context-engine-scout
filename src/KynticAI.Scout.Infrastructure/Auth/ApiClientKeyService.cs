using System.Security.Cryptography;
using System.Text.Json;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Domain.Saas;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.Infrastructure.Auth;

public sealed class ApiClientKeyService(
    ScoutDbContext dbContext,
    PasswordHashingService passwordHashingService,
    ICurrentActorService currentActorService,
    IBillingEnforcementService billingEnforcementService,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApiClientCreatedResult> CreateAsync(
        string tenantSlug,
        string? workspaceSlug,
        string displayName,
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken)
    {
        var actor = currentActorService.GetCurrentActor();
        var tenant = await GetTenantForActorAsync(tenantSlug, actor, cancellationToken);
        var workspace = await ResolveWorkspaceAsync(tenant.Id, workspaceSlug, cancellationToken);
        await EnsureWorkspaceAccessAsync(actor, tenant, workspace, cancellationToken);
        await billingEnforcementService.EnsureWithinLimitAsync(tenant.Id, BillingLimitMetric.ApiClients, 1, cancellationToken, workspace?.Id);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var clientId = $"scout_{tenant.Slug}_{Guid.NewGuid():N}"[..32];
        var apiKey = GenerateApiKey();
        var normalizedScopes = NormalizeScopes(scopes);
        var client = ApiClient.Create(
            tenant.Id,
            workspace?.Id,
            clientId,
            displayName,
            passwordHashingService.HashPassword(apiKey),
            JsonSerializer.Serialize(normalizedScopes, JsonOptions),
            utcNow);

        dbContext.ApiClients.Add(client);
        dbContext.AuditEvents.Add(AuditEvent.Create(
            tenant.Id,
            actor.Email,
            "auth.api_key.created",
            nameof(ApiClient),
            client.Id.ToString("D"),
            Guid.NewGuid().ToString("N"),
            JsonSerializer.Serialize(new
            {
                tenantSlug = tenant.Slug,
                workspaceSlug = workspace?.Slug,
                client.ClientId,
                scopes = normalizedScopes
            }, JsonOptions),
            null,
            null,
            utcNow));

        await dbContext.SaveChangesAsync(cancellationToken);
        return new ApiClientCreatedResult(
            client.Id,
            tenant.Id,
            workspace?.Id,
            client.ClientId,
            client.DisplayName,
            apiKey,
            normalizedScopes,
            utcNow);
    }

    public async Task<ApiClientRotatedResult> RotateAsync(
        string tenantSlug,
        string clientId,
        CancellationToken cancellationToken)
    {
        var actor = currentActorService.GetCurrentActor();
        var tenant = await GetTenantForActorAsync(tenantSlug, actor, cancellationToken);
        var client = await dbContext.ApiClients
            .Include(x => x.Workspace)
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.ClientId == clientId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"API client '{clientId}' was not found.");
        await EnsureWorkspaceAccessAsync(actor, tenant, client.Workspace, cancellationToken);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var apiKey = GenerateApiKey();
        client.RotateSecret(passwordHashingService.HashPassword(apiKey), utcNow);
        dbContext.AuditEvents.Add(AuditEvent.Create(
            tenant.Id,
            actor.Email,
            "auth.api_key.rotated",
            nameof(ApiClient),
            client.Id.ToString("D"),
            Guid.NewGuid().ToString("N"),
            JsonSerializer.Serialize(new { tenantSlug = tenant.Slug, client.ClientId }, JsonOptions),
            null,
            null,
            utcNow));
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ApiClientRotatedResult(client.Id, client.ClientId, apiKey, utcNow);
    }

    public async Task RevokeAsync(string tenantSlug, string clientId, CancellationToken cancellationToken)
    {
        var actor = currentActorService.GetCurrentActor();
        var tenant = await GetTenantForActorAsync(tenantSlug, actor, cancellationToken);
        var client = await dbContext.ApiClients
            .Include(x => x.Workspace)
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.ClientId == clientId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"API client '{clientId}' was not found.");
        await EnsureWorkspaceAccessAsync(actor, tenant, client.Workspace, cancellationToken);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        client.Revoke(utcNow);
        dbContext.AuditEvents.Add(AuditEvent.Create(
            tenant.Id,
            actor.Email,
            "auth.api_key.revoked",
            nameof(ApiClient),
            client.Id.ToString("D"),
            Guid.NewGuid().ToString("N"),
            JsonSerializer.Serialize(new { tenantSlug = tenant.Slug, client.ClientId }, JsonOptions),
            null,
            null,
            utcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApiClientSummaryResult>> ListAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        var actor = currentActorService.GetCurrentActor();
        var tenant = await GetTenantForActorAsync(tenantSlug, actor, cancellationToken);
        var clients = await dbContext.ApiClients
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        return clients
            .Where(client => actor.IsPlatformOwner || actor.WorkspaceId is null || client.WorkspaceId is null || client.WorkspaceId == actor.WorkspaceId)
            .Select(client => new ApiClientSummaryResult(
                client.Id,
                client.TenantId,
                client.WorkspaceId,
                client.ClientId,
                client.DisplayName,
                client.Status.ToString(),
                DeserializeScopes(client.ScopesJson),
                client.LastUsedAtUtc,
                client.RotatedAtUtc,
                client.RevokedAtUtc))
            .ToList();
    }

    private async Task<Tenant> GetTenantForActorAsync(string tenantSlug, ActorContext actor, CancellationToken cancellationToken)
    {
        var normalizedSlug = tenantSlug.Trim().ToLowerInvariant();
        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(x => x.Slug == normalizedSlug, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{tenantSlug}' was not found.");
        if (!actor.IsSystem && !actor.IsPlatformOwner && !string.Equals(actor.TenantSlug, normalizedSlug, StringComparison.Ordinal))
        {
            await AuditPermissionDeniedAsync(tenant.Id, actor, "cross-tenant-api-client", cancellationToken);
            throw new UnauthorizedAccessException("Cross-tenant access is not permitted.");
        }

        return tenant;
    }

    private async Task<Workspace?> ResolveWorkspaceAsync(Guid tenantId, string? workspaceSlug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspaceSlug))
        {
            return await dbContext.Workspaces
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.Name)
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Status == WorkspaceStatus.Active, cancellationToken);
        }

        var normalizedSlug = workspaceSlug.Trim().ToLowerInvariant();
        return await dbContext.Workspaces.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.Slug == normalizedSlug && x.Status == WorkspaceStatus.Active,
            cancellationToken) ?? throw new InvalidOperationException($"Workspace '{workspaceSlug}' was not found.");
    }

    private async Task EnsureWorkspaceAccessAsync(ActorContext actor, Tenant tenant, Workspace? workspace, CancellationToken cancellationToken)
    {
        if (actor.IsSystem || actor.IsPlatformOwner || actor.WorkspaceId is null || workspace is null || actor.WorkspaceId == workspace.Id)
        {
            return;
        }

        dbContext.AuditEvents.Add(AuditEvent.Create(
            tenant.Id,
            actor.Email,
            "auth.permission.denied",
            nameof(Workspace),
            workspace.Id.ToString("D"),
            Guid.NewGuid().ToString("N"),
            JsonSerializer.Serialize(new
            {
                reason = "workspace-scope",
                actor.WorkspaceId,
                requestedWorkspaceId = workspace.Id
            }, JsonOptions),
            null,
            null,
            timeProvider.GetUtcNow().UtcDateTime));
        await dbContext.SaveChangesAsync(cancellationToken);
        throw new UnauthorizedAccessException("Workspace access is not permitted.");
    }

    private async Task AuditPermissionDeniedAsync(Guid? tenantId, ActorContext actor, string reason, CancellationToken cancellationToken)
    {
        dbContext.AuditEvents.Add(AuditEvent.Create(
            tenantId,
            actor.Email,
            "auth.permission.denied",
            "Authorization",
            actor.SubjectId,
            Guid.NewGuid().ToString("N"),
            JsonSerializer.Serialize(new { reason, actor.TenantSlug, actor.WorkspaceId }, JsonOptions),
            null,
            null,
            timeProvider.GetUtcNow().UtcDateTime));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<string> NormalizeScopes(IReadOnlyList<string> scopes)
        => ApiScopes.Normalize(scopes);

    private static IReadOnlyList<string> DeserializeScopes(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string GenerateApiKey()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return "scout_live_" + Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }
}

public sealed record ApiClientCreatedResult(
    Guid Id,
    Guid TenantId,
    Guid? WorkspaceId,
    string ClientId,
    string DisplayName,
    string ApiKey,
    IReadOnlyList<string> Scopes,
    DateTime CreatedAtUtc);

public sealed record ApiClientRotatedResult(
    Guid Id,
    string ClientId,
    string ApiKey,
    DateTime RotatedAtUtc);

public sealed record ApiClientSummaryResult(
    Guid Id,
    Guid TenantId,
    Guid? WorkspaceId,
    string ClientId,
    string DisplayName,
    string Status,
    IReadOnlyList<string> Scopes,
    DateTime? LastUsedAtUtc,
    DateTime? RotatedAtUtc,
    DateTime? RevokedAtUtc);
