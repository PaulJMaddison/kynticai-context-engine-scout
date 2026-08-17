using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Domain.Saas;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.Infrastructure.Auth;

public sealed class WebhookSigningSecretService(
    ScoutDbContext dbContext,
    PasswordHashingService passwordHashingService,
    ICurrentActorService currentActorService,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WebhookSigningSecretCreatedResult> CreateAsync(string tenantSlug, string? workspaceSlug, string displayName, CancellationToken cancellationToken)
    {
        var actor = currentActorService.GetCurrentActor();
        var tenant = await GetTenantForActorAsync(tenantSlug, actor, cancellationToken);
        var workspace = await ResolveWorkspaceAsync(tenant.Id, workspaceSlug, cancellationToken);
        EnsureWorkspaceAccess(actor, tenant, workspace);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var rawSecret = GenerateSecret();
        var secret = WebhookSigningSecret.Create(
            tenant.Id,
            workspace?.Id,
            $"whsec_{tenant.Slug}_{Guid.NewGuid():N}"[..32],
            displayName,
            passwordHashingService.HashPassword(rawSecret),
            utcNow);
        dbContext.WebhookSigningSecrets.Add(secret);
        dbContext.AuditEvents.Add(AuditEvent.Create(tenant.Id, actor.Email, "webhook.secret.created", nameof(WebhookSigningSecret), secret.Id.ToString("D"), Guid.NewGuid().ToString("N"), JsonSerializer.Serialize(new { tenantSlug = tenant.Slug, workspaceSlug = workspace?.Slug, secret.SecretId }, JsonOptions), null, null, utcNow));
        await dbContext.SaveChangesAsync(cancellationToken);

        return new WebhookSigningSecretCreatedResult(secret.Id, tenant.Id, workspace?.Id, secret.SecretId, secret.DisplayName, rawSecret, secret.Status.ToString(), utcNow);
    }

    public async Task<IReadOnlyList<WebhookSigningSecretSummaryResult>> ListAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        var actor = currentActorService.GetCurrentActor();
        var tenant = await GetTenantForActorAsync(tenantSlug, actor, cancellationToken);
        var secrets = await dbContext.WebhookSigningSecrets
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        return secrets
            .Where(secret => actor.IsPlatformOwner || actor.WorkspaceId is null || secret.WorkspaceId is null || secret.WorkspaceId == actor.WorkspaceId)
            .Select(secret => new WebhookSigningSecretSummaryResult(secret.Id, secret.TenantId, secret.WorkspaceId, secret.SecretId, secret.DisplayName, secret.Status.ToString(), secret.LastUsedAtUtc, secret.RotatedAtUtc, secret.RevokedAtUtc))
            .ToList();
    }

    public async Task<WebhookSigningSecretRotatedResult> RotateAsync(string tenantSlug, string secretId, CancellationToken cancellationToken)
    {
        var actor = currentActorService.GetCurrentActor();
        var tenant = await GetTenantForActorAsync(tenantSlug, actor, cancellationToken);
        var secret = await GetSecretForMutationAsync(tenant.Id, secretId, actor, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var rawSecret = GenerateSecret();
        secret.RotateSecret(passwordHashingService.HashPassword(rawSecret), utcNow);
        dbContext.AuditEvents.Add(AuditEvent.Create(tenant.Id, actor.Email, "webhook.secret.rotated", nameof(WebhookSigningSecret), secret.Id.ToString("D"), Guid.NewGuid().ToString("N"), JsonSerializer.Serialize(new { tenantSlug = tenant.Slug, secret.SecretId }, JsonOptions), null, null, utcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new WebhookSigningSecretRotatedResult(secret.Id, secret.SecretId, rawSecret, utcNow);
    }

    public async Task RevokeAsync(string tenantSlug, string secretId, CancellationToken cancellationToken)
    {
        var actor = currentActorService.GetCurrentActor();
        var tenant = await GetTenantForActorAsync(tenantSlug, actor, cancellationToken);
        var secret = await GetSecretForMutationAsync(tenant.Id, secretId, actor, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        secret.Revoke(utcNow);
        dbContext.AuditEvents.Add(AuditEvent.Create(tenant.Id, actor.Email, "webhook.secret.revoked", nameof(WebhookSigningSecret), secret.Id.ToString("D"), Guid.NewGuid().ToString("N"), JsonSerializer.Serialize(new { tenantSlug = tenant.Slug, secret.SecretId }, JsonOptions), null, null, utcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<WebhookSignatureValidationResult> ValidateAsync(string tenantSlug, string? workspaceSlug, string secretId, string candidateSecret, string timestamp, string eventId, string body, string signature, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug)
            || string.IsNullOrWhiteSpace(secretId)
            || string.IsNullOrWhiteSpace(candidateSecret)
            || string.IsNullOrWhiteSpace(timestamp)
            || string.IsNullOrWhiteSpace(eventId)
            || string.IsNullOrWhiteSpace(signature))
        {
            return new WebhookSignatureValidationResult(false, "invalid_request");
        }

        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(x => x.Slug == tenantSlug.Trim().ToLowerInvariant(), cancellationToken);
        if (tenant is null)
        {
            return new WebhookSignatureValidationResult(false, "tenant_not_found");
        }

        var workspace = string.IsNullOrWhiteSpace(workspaceSlug)
            ? await dbContext.Workspaces.OrderByDescending(x => x.IsDefault).FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.Status == WorkspaceStatus.Active, cancellationToken)
            : await dbContext.Workspaces.FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.Slug == workspaceSlug.Trim().ToLowerInvariant() && x.Status == WorkspaceStatus.Active, cancellationToken);
        var secret = await dbContext.WebhookSigningSecrets.FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.SecretId == secretId.Trim(), cancellationToken);
        if (secret is null || secret.Status != WebhookSigningSecretStatus.Active)
        {
            await AuditValidationAsync(tenant.Id, workspace?.Id, secretId, "webhook.secret.denied", "revoked_or_missing", cancellationToken);
            return new WebhookSignatureValidationResult(false, "revoked_or_missing", tenant.Id, workspace?.Id, secretId);
        }

        if (workspace?.Id != secret.WorkspaceId && secret.WorkspaceId is not null)
        {
            await AuditValidationAsync(tenant.Id, workspace?.Id, secretId, "webhook.secret.denied", "workspace_mismatch", cancellationToken);
            return new WebhookSignatureValidationResult(false, "workspace_mismatch", tenant.Id, workspace?.Id, secretId);
        }

        var trimmedSecret = candidateSecret.Trim();
        if (!passwordHashingService.VerifyPassword(trimmedSecret, secret.SecretHash))
        {
            await AuditValidationAsync(tenant.Id, workspace?.Id, secretId, "webhook.signature.rejected", "bad_secret", cancellationToken);
            return new WebhookSignatureValidationResult(false, "bad_secret", tenant.Id, workspace?.Id, secretId);
        }

        var utcNow = timeProvider.GetUtcNow();
        if (!IsFresh(timestamp, utcNow))
        {
            await AuditValidationAsync(tenant.Id, workspace?.Id, secretId, "webhook.signature.rejected", "expired_timestamp", cancellationToken);
            return new WebhookSignatureValidationResult(false, "expired_timestamp", tenant.Id, workspace?.Id, secretId);
        }

        if (await dbContext.SourceSystemEvents.AnyAsync(x => x.TenantId == tenant.Id && x.EventId == eventId, cancellationToken))
        {
            await AuditValidationAsync(tenant.Id, workspace?.Id, secretId, "webhook.signature.rejected", "replayed_event_id", cancellationToken);
            return new WebhookSignatureValidationResult(false, "replayed_event_id", tenant.Id, workspace?.Id, secretId);
        }

        if (!VerifyHmac(trimmedSecret, timestamp, eventId, body ?? string.Empty, signature))
        {
            await AuditValidationAsync(tenant.Id, workspace?.Id, secretId, "webhook.signature.rejected", "bad_signature", cancellationToken);
            return new WebhookSignatureValidationResult(false, "bad_signature", tenant.Id, workspace?.Id, secretId);
        }

        secret.MarkUsed(utcNow.UtcDateTime);
        await AuditValidationAsync(tenant.Id, workspace?.Id, secretId, "webhook.signature.accepted", "accepted", cancellationToken);
        return new WebhookSignatureValidationResult(true, "accepted", tenant.Id, workspace?.Id, secretId);
    }

    private async Task<WebhookSigningSecret> GetSecretForMutationAsync(Guid tenantId, string secretId, ActorContext actor, CancellationToken cancellationToken)
    {
        var secret = await dbContext.WebhookSigningSecrets.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.SecretId == secretId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Webhook signing secret '{secretId}' was not found.");
        if (!actor.IsPlatformOwner && actor.WorkspaceId is not null && secret.WorkspaceId is not null && actor.WorkspaceId != secret.WorkspaceId)
        {
            throw new UnauthorizedAccessException("Workspace access is not permitted.");
        }

        return secret;
    }

    private async Task<Tenant> GetTenantForActorAsync(string tenantSlug, ActorContext actor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug))
        {
            throw new InvalidOperationException("Tenant slug is required.");
        }

        var normalizedSlug = tenantSlug.Trim().ToLowerInvariant();
        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(x => x.Slug == normalizedSlug, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{tenantSlug}' was not found.");
        if (!actor.IsSystem && !actor.IsPlatformOwner && !string.Equals(actor.TenantSlug, normalizedSlug, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Cross-tenant access is not permitted.");
        }

        return tenant;
    }

    private async Task<Workspace?> ResolveWorkspaceAsync(Guid tenantId, string? workspaceSlug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspaceSlug))
        {
            return await dbContext.Workspaces.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Status == WorkspaceStatus.Active, cancellationToken);
        }

        var normalizedSlug = workspaceSlug.Trim().ToLowerInvariant();
        return await dbContext.Workspaces.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Slug == normalizedSlug && x.Status == WorkspaceStatus.Active, cancellationToken)
            ?? throw new InvalidOperationException($"Workspace '{workspaceSlug}' was not found.");
    }

    private void EnsureWorkspaceAccess(ActorContext actor, Tenant tenant, Workspace? workspace)
    {
        if (actor.IsSystem || actor.IsPlatformOwner || actor.WorkspaceId is null || workspace is null || actor.WorkspaceId == workspace.Id)
        {
            return;
        }

        throw new UnauthorizedAccessException("Workspace access is not permitted.");
    }

    private async Task AuditValidationAsync(Guid tenantId, Guid? workspaceId, string? secretId, string action, string reason, CancellationToken cancellationToken)
    {
        dbContext.AuditEvents.Add(AuditEvent.Create(tenantId, "webhook", action, "WebhookSigningSecret", secretId ?? "unknown", Guid.NewGuid().ToString("N"), JsonSerializer.Serialize(new { reason, workspaceId, secretId }, JsonOptions), null, null, timeProvider.GetUtcNow().UtcDateTime));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static bool VerifyLegacyApiKeyHmac(string apiKey, string timestamp, string body, string signature)
    {
        if (string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(timestamp)
            || string.IsNullOrWhiteSpace(signature)
            || !IsFresh(timestamp, TimeProvider.System.GetUtcNow()))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiKey.Trim()));
        var expected = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body ?? string.Empty}"))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    private static bool VerifyHmac(string secret, string timestamp, string eventId, string body, string signature)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{eventId}.{body}"))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    internal static bool IsFresh(string timestamp, DateTimeOffset utcNow) =>
        DateTimeOffset.TryParse(timestamp, out var parsedTimestamp)
        && Math.Abs((utcNow - parsedTimestamp.ToUniversalTime()).TotalMinutes) <= 5;

    private static string GenerateSecret()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return "scout_whsec_" + Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }
}
