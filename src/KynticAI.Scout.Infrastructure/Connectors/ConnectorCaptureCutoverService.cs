using System.Security.Cryptography;
using System.Text;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.Infrastructure.Connectors;

/// <summary>
/// Coordinates the customer-local persistent ownership barrier for one connector during a
/// Scout -> Fortress cutover.
///
/// This service never stores the raw cutover token. It binds ownership to the exact completed
/// Scout generation, completion timestamp, high-water hash, caller-provided epoch and SHA-256 of
/// the token. It refuses to pause/transfer while a Scout capture lease is active.
/// </summary>
internal sealed class ConnectorCaptureCutoverService(
    ScoutDbContext dbContext,
    IClock clock)
{
    public async Task<ConnectorCaptureOwnership> PauseScoutAsync(
        Guid tenantId,
        Guid connectorInstallationId,
        Guid cutoverEpoch,
        string cutoverToken,
        CancellationToken cancellationToken)
    {
        ValidateRequest(tenantId, connectorInstallationId, cutoverEpoch, cutoverToken);
        var now = EnsureUtc(clock.UtcNow);
        var checkpoint = await LoadCheckpointAsync(tenantId, connectorInstallationId, cancellationToken);
        EnsureCompletedStableGeneration(checkpoint, now);

        var highWaterHash = Sha256(checkpoint.HighWaterMarkJson);
        var tokenHash = Sha256(cutoverToken);
        var ownership = await dbContext.ConnectorCaptureOwnerships
            .SingleOrDefaultAsync(x => x.TenantId == tenantId
                && x.ConnectorInstallationId == connectorInstallationId,
                cancellationToken);

        if (ownership is null)
        {
            ownership = ConnectorCaptureOwnership.CreateForCutover(
                tenantId,
                connectorInstallationId,
                checkpoint.Generation,
                checkpoint.LastFullSourceCompletedAtUtc!.Value,
                highWaterHash,
                cutoverEpoch,
                tokenHash,
                now);
            ownership.PauseScoutForCutover(now);
            dbContext.ConnectorCaptureOwnerships.Add(ownership);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ownership;
        }

        ownership.AssertBinding(
            checkpoint.Generation,
            checkpoint.LastFullSourceCompletedAtUtc!.Value,
            highWaterHash,
            cutoverEpoch,
            tokenHash);

        switch (ownership.State)
        {
            case ConnectorCaptureOwnershipState.ScoutActive:
                ownership.PauseScoutForCutover(now);
                await dbContext.SaveChangesAsync(cancellationToken);
                break;
            case ConnectorCaptureOwnershipState.ScoutPausedForCutover:
                // Exact retry is idempotent. No mutation is required.
                break;
            case ConnectorCaptureOwnershipState.FortressOwned:
                throw new InvalidOperationException(
                    "Connector is already Fortress-owned; Scout cutover pause cannot be replayed as a new transfer.");
            default:
                throw new InvalidOperationException($"Unsupported connector ownership state {ownership.State}.");
        }

        return ownership;
    }

    public async Task<ConnectorCaptureOwnership> TransferToFortressAsync(
        Guid tenantId,
        Guid connectorInstallationId,
        Guid cutoverEpoch,
        string cutoverToken,
        CancellationToken cancellationToken)
    {
        ValidateRequest(tenantId, connectorInstallationId, cutoverEpoch, cutoverToken);
        var now = EnsureUtc(clock.UtcNow);
        var checkpoint = await LoadCheckpointAsync(tenantId, connectorInstallationId, cancellationToken);
        EnsureNoActiveScoutLease(checkpoint, now);

        var ownership = await dbContext.ConnectorCaptureOwnerships
            .SingleOrDefaultAsync(x => x.TenantId == tenantId
                && x.ConnectorInstallationId == connectorInstallationId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "No persisted Scout-paused cutover barrier exists for this connector.");

        var tokenHash = Sha256(cutoverToken);
        ownership.AssertBinding(
            checkpoint.Generation,
            checkpoint.LastFullSourceCompletedAtUtc
                ?? throw new InvalidOperationException("Selected Scout generation has no completion timestamp."),
            Sha256(checkpoint.HighWaterMarkJson),
            cutoverEpoch,
            tokenHash);

        if (ownership.State == ConnectorCaptureOwnershipState.FortressOwned)
        {
            // Exact retry after a committed transfer is safe and idempotent when the binding is
            // still identical.
            return ownership;
        }

        ownership.TransferToFortress(cutoverEpoch, tokenHash, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ownership;
    }

    public async Task<ConnectorCaptureOwnership> AbortCutoverAndResumeScoutAsync(
        Guid tenantId,
        Guid connectorInstallationId,
        Guid cutoverEpoch,
        string cutoverToken,
        CancellationToken cancellationToken)
    {
        ValidateRequest(tenantId, connectorInstallationId, cutoverEpoch, cutoverToken);
        var now = EnsureUtc(clock.UtcNow);
        var checkpoint = await LoadCheckpointAsync(tenantId, connectorInstallationId, cancellationToken);
        EnsureNoActiveScoutLease(checkpoint, now);

        var ownership = await dbContext.ConnectorCaptureOwnerships
            .SingleOrDefaultAsync(x => x.TenantId == tenantId
                && x.ConnectorInstallationId == connectorInstallationId,
                cancellationToken)
            ?? throw new InvalidOperationException("No cutover ownership barrier exists to abort.");

        ownership.AssertBinding(
            checkpoint.Generation,
            checkpoint.LastFullSourceCompletedAtUtc
                ?? throw new InvalidOperationException("Selected Scout generation has no completion timestamp."),
            Sha256(checkpoint.HighWaterMarkJson),
            cutoverEpoch,
            Sha256(cutoverToken));

        ownership.ResumeScoutAfterAbortedCutover(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ownership;
    }

    private async Task<ConnectorCaptureCheckpoint> LoadCheckpointAsync(
        Guid tenantId,
        Guid connectorInstallationId,
        CancellationToken cancellationToken)
        => await dbContext.ConnectorCaptureCheckpoints
            .SingleOrDefaultAsync(x => x.TenantId == tenantId
                && x.ConnectorInstallationId == connectorInstallationId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Connector has no local capture checkpoint; cutover ownership cannot be proven.");

    private static void EnsureCompletedStableGeneration(
        ConnectorCaptureCheckpoint checkpoint,
        DateTime utcNow)
    {
        if (checkpoint.Generation <= 0 || checkpoint.LastFullSourceCompletedAtUtc is null)
            throw new InvalidOperationException(
                "Connector has no completed FULL_SOURCE generation to bind to the cutover.");
        if (!string.IsNullOrWhiteSpace(checkpoint.ContinuationToken))
            throw new InvalidOperationException(
                "Connector has an in-flight paged FULL_SOURCE generation; complete or abandon it before cutover.");
        if (!string.IsNullOrWhiteSpace(checkpoint.LastError))
            throw new InvalidOperationException(
                "Connector checkpoint records a capture error; repair/recapture before cutover.");

        EnsureNoActiveScoutLease(checkpoint, utcNow);
    }

    private static void EnsureNoActiveScoutLease(
        ConnectorCaptureCheckpoint checkpoint,
        DateTime utcNow)
    {
        if (checkpoint.LeaseExpiresAtUtc.HasValue && checkpoint.LeaseExpiresAtUtc.Value > utcNow)
        {
            throw new InvalidOperationException(
                "Scout connector capture lease is still active; wait for/release the worker before changing source ownership.");
        }
    }

    private static void ValidateRequest(
        Guid tenantId,
        Guid connectorInstallationId,
        Guid cutoverEpoch,
        string cutoverToken)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (connectorInstallationId == Guid.Empty)
            throw new ArgumentException("Connector installation id is required.", nameof(connectorInstallationId));
        if (cutoverEpoch == Guid.Empty)
            throw new ArgumentException("Cutover epoch is required.", nameof(cutoverEpoch));
        if (string.IsNullOrWhiteSpace(cutoverToken) || cutoverToken.Length < 32)
            throw new ArgumentException("Cutover token must contain at least 32 characters of local entropy.", nameof(cutoverToken));
    }

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
