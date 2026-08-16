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
/// the token.
///
/// Pause is made race-safe with the existing connector lease: the cutover operation first becomes
/// the lease owner, commits the paused ownership row while normal Scout workers are excluded, then
/// releases the lease. A worker that was already active prevents cutover from starting.
/// </summary>
internal sealed class ConnectorCaptureCutoverService(
    ScoutDbContext dbContext,
    IClock clock)
{
    private static readonly TimeSpan CutoverLeaseDuration = TimeSpan.FromMinutes(5);

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
        EnsureCompletedGeneration(checkpoint);

        var barrierOwner = $"cutover:{cutoverEpoch:N}";
        if (!checkpoint.TryAcquireLease(barrierOwner, CutoverLeaseDuration, now))
        {
            throw new InvalidOperationException(
                "Scout connector capture lease is still active; wait for/release the worker before changing source ownership.");
        }

        var leaseCommitted = false;
        var ownershipCommitted = false;
        try
        {
            // Persist the cutover lease first. Connector workers use the same lease concurrency
            // tokens, so one cannot successfully enter the source while this barrier is held.
            await dbContext.SaveChangesAsync(cancellationToken);
            leaseCommitted = true;

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
                ownershipCommitted = true;
                return ownership;
            }

            switch (ownership.State)
            {
                case ConnectorCaptureOwnershipState.ScoutActive:
                    // A previous cutover may have been explicitly aborted. Bind the new attempt to
                    // the current completed generation/high-water mark and new epoch/token before
                    // pausing Scout again.
                    ownership.RebindForCutover(
                        checkpoint.Generation,
                        checkpoint.LastFullSourceCompletedAtUtc!.Value,
                        highWaterHash,
                        cutoverEpoch,
                        tokenHash,
                        now);
                    ownership.PauseScoutForCutover(now);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    ownershipCommitted = true;
                    break;
                case ConnectorCaptureOwnershipState.ScoutPausedForCutover:
                    ownership.AssertBinding(
                        checkpoint.Generation,
                        checkpoint.LastFullSourceCompletedAtUtc!.Value,
                        highWaterHash,
                        cutoverEpoch,
                        tokenHash);
                    // Exact retry is idempotent and the ownership was committed by the earlier run.
                    ownershipCommitted = true;
                    break;
                case ConnectorCaptureOwnershipState.FortressOwned:
                    throw new InvalidOperationException(
                        "Connector is already Fortress-owned; Scout cutover pause cannot be replayed as a new transfer.");
                default:
                    throw new InvalidOperationException($"Unsupported connector ownership state {ownership.State}.");
            }

            return ownership;
        }
        finally
        {
            if (!ownershipCommitted)
            {
                // A failed/cancelled ownership SaveChanges must not be flushed accidentally by the
                // subsequent lease-release SaveChanges. Added rows are detached; modified rows are
                // reloaded from their last durable database version first.
                foreach (var entry in dbContext.ChangeTracker
                             .Entries<ConnectorCaptureOwnership>()
                             .Where(entry => entry.Entity.TenantId == tenantId
                                 && entry.Entity.ConnectorInstallationId == connectorInstallationId)
                             .ToArray())
                {
                    if (entry.State == EntityState.Added)
                    {
                        entry.State = EntityState.Detached;
                    }
                    else if (entry.State == EntityState.Modified)
                    {
                        await entry.ReloadAsync(CancellationToken.None);
                    }
                }
            }

            // Only release a lease that was actually committed. If the initial concurrency/database
            // write failed, a cleanup SaveChanges could mask that original failure or mutate a lease
            // owned by somebody else. In that case this scoped context is simply allowed to unwind.
            if (leaseCommitted)
            {
                // Ownership is committed before this lease is released. If the pause failed after
                // the lease commit, releasing here restores normal Scout availability rather than
                // leaving a cutover lease stranded.
                checkpoint.ReleaseLease(barrierOwner, EnsureUtc(clock.UtcNow));
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
        }
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

    private static void EnsureCompletedGeneration(ConnectorCaptureCheckpoint checkpoint)
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
