using System.Security.Claims;
using System.Text.Json;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.Infrastructure.Auth;

public sealed class AuthenticationService(
    ScoutDbContext dbContext,
    PasswordHashingService passwordHashingService,
    JwtTokenService jwtTokenService,
    TimeProvider timeProvider)
{
    private const string InvalidCredentialsMessage = "Invalid tenant or credentials.";
    private const int MaximumTenantSlugLength = 128;
    private const int MaximumEmailLength = 320;
    private const int MaximumPasswordLength = 4_096;

    public async Task<LoginResult> LoginAsync(string tenantSlug, string email, string password, CancellationToken cancellationToken)
    {
        var normalizedTenantSlug = tenantSlug?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        if (string.IsNullOrWhiteSpace(normalizedTenantSlug)
            || string.IsNullOrWhiteSpace(normalizedEmail)
            || string.IsNullOrWhiteSpace(password)
            || normalizedTenantSlug.Length > MaximumTenantSlugLength
            || normalizedEmail.Length > MaximumEmailLength
            || password.Length > MaximumPasswordLength)
        {
            await RecordFailedLoginAsync(null, normalizedEmail, normalizedTenantSlug, utcNow, cancellationToken);
            throw new InvalidOperationException(InvalidCredentialsMessage);
        }

        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(x => x.Slug == normalizedTenantSlug, cancellationToken);
        if (tenant is null)
        {
            await RecordFailedLoginAsync(null, normalizedEmail, normalizedTenantSlug, utcNow, cancellationToken);
            throw new InvalidOperationException(InvalidCredentialsMessage);
        }

        var account = await dbContext.OperatorAccounts
            .FirstOrDefaultAsync(
                x => x.TenantId == tenant.Id && x.Email == normalizedEmail && x.IsActive,
                cancellationToken);
        if (account is null)
        {
            await RecordFailedLoginAsync(tenant.Id, normalizedEmail, normalizedTenantSlug, utcNow, cancellationToken);
            throw new InvalidOperationException(InvalidCredentialsMessage);
        }

        if (!passwordHashingService.VerifyPassword(password, account.PasswordHash))
        {
            await RecordFailedLoginAsync(tenant.Id, normalizedEmail, normalizedTenantSlug, utcNow, cancellationToken);
            throw new InvalidOperationException(InvalidCredentialsMessage);
        }

        var workspace = await dbContext.WorkspaceMembers
            .AsNoTracking()
            .Include(x => x.Workspace)
            .Where(x => x.TenantId == tenant.Id && x.OperatorAccountId == account.Id && x.Workspace.Status == WorkspaceStatus.Active)
            .OrderByDescending(x => x.Workspace.IsDefault)
            .ThenBy(x => x.Workspace.Name)
            .Select(x => x.Workspace)
            .FirstOrDefaultAsync(cancellationToken);

        account.MarkLogin(utcNow);
        var token = jwtTokenService.CreateToken(tenant, account, workspace);

        dbContext.AuditEvents.Add(AuditEvent.Create(
            tenant.Id,
            account.Email,
            "auth.login.succeeded",
            nameof(OperatorAccount),
            account.Id.ToString("D"),
            Guid.NewGuid().ToString("N"),
            JsonSerializer.Serialize(new
            {
                tenantSlug = tenant.Slug,
                workspaceSlug = workspace?.Slug,
                account.Email,
                role = RoleNames.ToClaimValue(account.Role)
            }),
            null,
            JsonSerializer.Serialize(new { token.ExpiresAtUtc }),
            utcNow));

        await dbContext.SaveChangesAsync(cancellationToken);

        return new LoginResult(
            token.AccessToken,
            token.ExpiresAtUtc,
            new AuthenticatedOperator(
                tenant.Id,
                tenant.Slug,
                workspace?.Id,
                workspace?.Slug,
                account.Id,
                account.Email,
                account.DisplayName,
                RoleNames.ToClaimValue(account.Role)));
    }

    public async Task<AuthenticatedOperator?> GetCurrentOperatorAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var accountIdValue = principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(accountIdValue, out var accountId))
        {
            return null;
        }

        var account = await dbContext.OperatorAccounts
            .Include(x => x.Tenant)
            .FirstOrDefaultAsync(x => x.Id == accountId && x.IsActive, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var workspace = await dbContext.WorkspaceMembers
            .AsNoTracking()
            .Include(x => x.Workspace)
            .Where(x => x.TenantId == account.TenantId && x.OperatorAccountId == account.Id && x.Workspace.Status == WorkspaceStatus.Active)
            .OrderByDescending(x => x.Workspace.IsDefault)
            .ThenBy(x => x.Workspace.Name)
            .Select(x => x.Workspace)
            .FirstOrDefaultAsync(cancellationToken);

        return new AuthenticatedOperator(
            account.TenantId,
            account.Tenant.Slug,
            workspace?.Id,
            workspace?.Slug,
            account.Id,
            account.Email,
            account.DisplayName,
            RoleNames.ToClaimValue(account.Role));
    }

    private async Task RecordFailedLoginAsync(
        Guid? tenantId,
        string normalizedEmail,
        string normalizedTenantSlug,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var auditEmail = Bound(normalizedEmail, MaximumEmailLength, "anonymous");
        var auditTenantSlug = Bound(normalizedTenantSlug, MaximumTenantSlugLength, string.Empty);
        dbContext.AuditEvents.Add(AuditEvent.Create(
            tenantId,
            auditEmail,
            "auth.login.failed",
            nameof(OperatorAccount),
            auditEmail == "anonymous" ? "unknown" : auditEmail,
            Guid.NewGuid().ToString("N"),
            JsonSerializer.Serialize(new
            {
                tenantSlug = auditTenantSlug,
                email = auditEmail == "anonymous" ? string.Empty : auditEmail
            }),
            null,
            null,
            utcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Bound(string value, int maximumLength, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}

public sealed record LoginResult(
    string AccessToken,
    DateTime ExpiresAtUtc,
    AuthenticatedOperator Operator);

public sealed record AuthenticatedOperator(
    Guid TenantId,
    string TenantSlug,
    Guid? WorkspaceSlug,
    string? WorkspaceSlugValue,
    Guid OperatorAccountId,
    string Email,
    string DisplayName,
    string Role);