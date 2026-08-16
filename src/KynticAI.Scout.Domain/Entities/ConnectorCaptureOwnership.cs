using KynticAI.Scout.Domain.Common;
using KynticAI.Scout.Domain.Enums;

namespace KynticAI.Scout.Domain.Entities;

/// <summary>
/// Local, fail-closed ownership barrier for one connector during a Scout -> Fortress cutover.
///
/// This entity deliberately stores only hashes/epochs and source-position metadata. The raw
/// cutover token, connector credentials and customer payloads are never persisted here.
///
/// Runtime/EF wiring is intentionally separate: until the generated migration exists, this class
/// is a domain contract only and must not be treated as an active database barrier.
/// </summary>
public sealed class ConnectorCaptureOwnership : AuditedTenantEntity
{
    private ConnectorCaptureOwnership() { }

    public Guid ConnectorInstallationId { get; private set; }
    public ConnectorCaptureOwnershipState State { get; private set; }
    public long SelectedGeneration { get; private set; }
    public DateTime SnapshotCompletedAtUtc { get; private set; }
    public string HighWaterMarkSha256 { get; private set; } = string.Empty;
    public Guid CutoverEpoch { get; private set; }
    public string CutoverTokenSha256 { get; private set; } = string.Empty;
    public DateTime? ScoutPausedAtUtc { get; private set; }
    public DateTime? FortressOwnedAtUtc { get; private set; }

    public bool ScoutMayCapture => State == ConnectorCaptureOwnershipState.ScoutActive;
    public bool FortressMayCapture => State == ConnectorCaptureOwnershipState.FortressOwned;

    public static ConnectorCaptureOwnership CreateForCutover(
        Guid tenantId,
        Guid connectorInstallationId,
        long selectedGeneration,
        DateTime snapshotCompletedAtUtc,
        string highWaterMarkSha256,
        Guid cutoverEpoch,
        string cutoverTokenSha256,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (connectorInstallationId == Guid.Empty)
            throw new ArgumentException("Connector installation id is required.", nameof(connectorInstallationId));
        if (selectedGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(selectedGeneration), "Selected generation must be positive.");
        if (snapshotCompletedAtUtc == default)
            throw new ArgumentException("Snapshot completion time is required.", nameof(snapshotCompletedAtUtc));
        if (cutoverEpoch == Guid.Empty)
            throw new ArgumentException("Cutover epoch is required.", nameof(cutoverEpoch));

        snapshotCompletedAtUtc = EnsureUtc(snapshotCompletedAtUtc);
        utcNow = EnsureUtc(utcNow);
        if (utcNow < snapshotCompletedAtUtc)
            throw new ArgumentException("Cutover state cannot be created before the selected snapshot completed.", nameof(utcNow));

        var ownership = new ConnectorCaptureOwnership
        {
            TenantId = tenantId,
            ConnectorInstallationId = connectorInstallationId,
            State = ConnectorCaptureOwnershipState.ScoutActive,
            SelectedGeneration = selectedGeneration,
            SnapshotCompletedAtUtc = snapshotCompletedAtUtc,
            HighWaterMarkSha256 = NormaliseSha256(highWaterMarkSha256, nameof(highWaterMarkSha256)),
            CutoverEpoch = cutoverEpoch,
            CutoverTokenSha256 = NormaliseSha256(cutoverTokenSha256, nameof(cutoverTokenSha256))
        };
        ownership.SetAuditTimestamps(utcNow);
        return ownership;
    }

    /// <summary>
    /// Records the durable cutover barrier after Scout has stopped acquiring/renewing the source
    /// capture lease at the selected generation/high-water mark.
    /// </summary>
    public void PauseScoutForCutover(DateTime utcNow)
    {
        if (State != ConnectorCaptureOwnershipState.ScoutActive)
            throw new InvalidOperationException($"Cannot pause Scout from ownership state {State}.");

        utcNow = EnsureUtc(utcNow);
        if (utcNow < SnapshotCompletedAtUtc)
            throw new InvalidOperationException("Scout pause cannot predate the selected snapshot completion.");

        State = ConnectorCaptureOwnershipState.ScoutPausedForCutover;
        ScoutPausedAtUtc = utcNow;
        FortressOwnedAtUtc = null;
        SetAuditTimestamps(utcNow);
    }

    /// <summary>
    /// Transfers source ownership to Fortress only after the durable Scout pause exists. The
    /// caller must prove the same epoch and token hash that were bound when the cutover began.
    /// </summary>
    public void TransferToFortress(Guid cutoverEpoch, string cutoverTokenSha256, DateTime utcNow)
    {
        if (State != ConnectorCaptureOwnershipState.ScoutPausedForCutover || ScoutPausedAtUtc is null)
            throw new InvalidOperationException("Fortress ownership requires a persisted Scout-paused cutover barrier.");
        if (cutoverEpoch != CutoverEpoch)
            throw new InvalidOperationException("Cutover epoch does not match the persisted ownership barrier.");
        if (!string.Equals(
                NormaliseSha256(cutoverTokenSha256, nameof(cutoverTokenSha256)),
                CutoverTokenSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cutover token does not match the persisted ownership barrier.");
        }

        utcNow = EnsureUtc(utcNow);
        if (utcNow < ScoutPausedAtUtc.Value)
            throw new InvalidOperationException("Fortress ownership cannot predate the Scout pause.");

        State = ConnectorCaptureOwnershipState.FortressOwned;
        FortressOwnedAtUtc = utcNow;
        SetAuditTimestamps(utcNow);
    }

    /// <summary>
    /// Allows an operator to abandon a cutover before ownership has transferred. Once Fortress
    /// owns the source, Scout cannot silently reclaim it through this state machine.
    /// </summary>
    public void ResumeScoutAfterAbortedCutover(DateTime utcNow)
    {
        if (State != ConnectorCaptureOwnershipState.ScoutPausedForCutover)
            throw new InvalidOperationException("Only a paused, not-yet-transferred cutover can resume Scout capture.");

        utcNow = EnsureUtc(utcNow);
        if (ScoutPausedAtUtc.HasValue && utcNow < ScoutPausedAtUtc.Value)
            throw new InvalidOperationException("Scout resume cannot predate the recorded pause.");

        State = ConnectorCaptureOwnershipState.ScoutActive;
        ScoutPausedAtUtc = null;
        FortressOwnedAtUtc = null;
        SetAuditTimestamps(utcNow);
    }

    public void AssertBinding(
        long selectedGeneration,
        DateTime snapshotCompletedAtUtc,
        string highWaterMarkSha256,
        Guid cutoverEpoch,
        string cutoverTokenSha256)
    {
        if (selectedGeneration != SelectedGeneration
            || EnsureUtc(snapshotCompletedAtUtc) != SnapshotCompletedAtUtc
            || !string.Equals(
                NormaliseSha256(highWaterMarkSha256, nameof(highWaterMarkSha256)),
                HighWaterMarkSha256,
                StringComparison.Ordinal)
            || cutoverEpoch != CutoverEpoch
            || !string.Equals(
                NormaliseSha256(cutoverTokenSha256, nameof(cutoverTokenSha256)),
                CutoverTokenSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cutover request does not match the persisted source-ownership binding.");
        }
    }

    private static string NormaliseSha256(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != 64
            || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Value must be a 64-character SHA-256 hex digest.", parameterName);
        }
        return value.ToLowerInvariant();
    }

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
