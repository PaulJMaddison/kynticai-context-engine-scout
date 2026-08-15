using KynticAI.Scout.Domain.Common;

namespace KynticAI.Scout.Domain.Entities;

/// <summary>
/// Customer-local cursor/lease state for whole-source connector capture. It is deliberately
/// independent of KynticAI Cloud: source positions, cursors and capture history remain in the
/// sovereign data plane. One active lease per connector installation prevents Scout/Fortress
/// cutover from creating two independent pollers for the same source.
/// </summary>
public sealed class ConnectorCaptureCheckpoint : AuditedTenantEntity
{
    private ConnectorCaptureCheckpoint() { }

    public Guid ConnectorInstallationId { get; private set; }
    public Guid DataSourceId { get; private set; }
    public string CaptureProfile { get; private set; } = string.Empty;
    public string CaptureProfileVersion { get; private set; } = string.Empty;
    public string CoverageScope { get; private set; } = string.Empty;
    public string HistoryCompleteness { get; private set; } = string.Empty;

    /// <summary>
    /// Whether one completed FULL_SOURCE generation represented a coherent current-state view.
    /// This is intentionally independent from HistoryCompleteness. A source can have no change
    /// history yet still provide an immutable point-in-time snapshot, or enumerate all rows from
    /// a live table/API without being a point-in-time view.
    /// </summary>
    public string CurrentStateConsistency { get; private set; } = "UNKNOWN";

    /// <summary>
    /// Storage contract of the last completed full-source generation. Legacy Scout rows were
    /// retained in jsonb only and therefore cannot prove byte-identical replay after a database
    /// round trip. New generations set this to exact-text.v1 only after every retained capture
    /// record has an exact local payload-evidence sidecar.
    /// </summary>
    public string PayloadStorageContract { get; private set; } = "legacy-jsonb.v0";

    public string? ContinuationToken { get; private set; }
    public string HighWaterMarkJson { get; private set; } = "{}";
    public DateTime? EarliestAvailableAtUtc { get; private set; }
    public DateTime? EarliestCapturedAtUtc { get; private set; }
    public DateTime? LatestCapturedAtUtc { get; private set; }
    public DateTime? LastFullSourceCompletedAtUtc { get; private set; }
    public long CapturedRecordCount { get; private set; }
    public long Generation { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public string? LastError { get; private set; }

    public static ConnectorCaptureCheckpoint Create(
        Guid tenantId,
        Guid connectorInstallationId,
        Guid dataSourceId,
        string captureProfile,
        string captureProfileVersion,
        string coverageScope,
        string historyCompleteness,
        DateTime? earliestAvailableAtUtc,
        DateTime utcNow,
        string payloadStorageContract = "legacy-jsonb.v0",
        string currentStateConsistency = "UNKNOWN")
    {
        var checkpoint = new ConnectorCaptureCheckpoint
        {
            TenantId = tenantId,
            ConnectorInstallationId = connectorInstallationId,
            DataSourceId = dataSourceId,
            CaptureProfile = captureProfile.Trim(),
            CaptureProfileVersion = captureProfileVersion.Trim(),
            CoverageScope = coverageScope.Trim(),
            HistoryCompleteness = historyCompleteness.Trim(),
            CurrentStateConsistency = string.IsNullOrWhiteSpace(currentStateConsistency)
                ? "UNKNOWN"
                : currentStateConsistency.Trim(),
            PayloadStorageContract = string.IsNullOrWhiteSpace(payloadStorageContract)
                ? "legacy-jsonb.v0"
                : payloadStorageContract.Trim(),
            EarliestAvailableAtUtc = earliestAvailableAtUtc,
            HighWaterMarkJson = "{}"
        };
        checkpoint.SetAuditTimestamps(utcNow);
        return checkpoint;
    }

    public bool TryAcquireLease(string owner, TimeSpan duration, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));
        if (LeaseExpiresAtUtc.HasValue && LeaseExpiresAtUtc.Value > utcNow
            && !string.Equals(LeaseOwner, owner, StringComparison.Ordinal))
            return false;
        LeaseOwner = owner.Trim();
        LeaseExpiresAtUtc = utcNow.Add(duration);
        SetAuditTimestamps(utcNow);
        return true;
    }

    public void RenewLease(string owner, TimeSpan duration, DateTime utcNow)
    {
        EnsureLeaseOwner(owner, utcNow);
        LeaseExpiresAtUtc = utcNow.Add(duration);
        SetAuditTimestamps(utcNow);
    }

    /// <summary>
    /// Persist the semantic contract observed on a non-empty page so an empty terminal page
    /// cannot erase it. The coordinator rejects contradictory page semantics inside one
    /// generation before calling this method.
    /// </summary>
    public void ObserveCaptureSemantics(
        string owner,
        string historyCompleteness,
        string currentStateConsistency,
        DateTime? earliestAvailableAtUtc,
        DateTime utcNow)
    {
        EnsureLeaseOwner(owner, utcNow);
        if (string.IsNullOrWhiteSpace(historyCompleteness))
            throw new ArgumentException("History completeness is required.", nameof(historyCompleteness));
        if (string.IsNullOrWhiteSpace(currentStateConsistency))
            throw new ArgumentException("Current-state consistency is required.", nameof(currentStateConsistency));
        HistoryCompleteness = historyCompleteness.Trim();
        CurrentStateConsistency = currentStateConsistency.Trim();
        EarliestAvailableAtUtc = Min(EarliestAvailableAtUtc, earliestAvailableAtUtc);
        SetAuditTimestamps(utcNow);
    }

    public void ObserveEarliestAvailable(string owner, DateTime? earliestAvailableAtUtc, DateTime utcNow)
    {
        EnsureLeaseOwner(owner, utcNow);
        EarliestAvailableAtUtc = Min(EarliestAvailableAtUtc, earliestAvailableAtUtc);
        SetAuditTimestamps(utcNow);
    }

    public void Advance(
        string owner,
        string? continuationToken,
        string highWaterMarkJson,
        long capturedRecords,
        DateTime? earliestCapturedAtUtc,
        DateTime? latestCapturedAtUtc,
        DateTime utcNow)
    {
        EnsureLeaseOwner(owner, utcNow);
        if (capturedRecords < 0)
            throw new ArgumentOutOfRangeException(nameof(capturedRecords));
        ContinuationToken = string.IsNullOrWhiteSpace(continuationToken) ? null : continuationToken;
        HighWaterMarkJson = string.IsNullOrWhiteSpace(highWaterMarkJson) ? "{}" : highWaterMarkJson;
        CapturedRecordCount = checked(CapturedRecordCount + capturedRecords);
        EarliestCapturedAtUtc = Min(EarliestCapturedAtUtc, earliestCapturedAtUtc);
        LatestCapturedAtUtc = Max(LatestCapturedAtUtc, latestCapturedAtUtc);
        LastError = null;
        SetAuditTimestamps(utcNow);
    }

    public void CompleteFullSourceGeneration(
        string owner,
        string highWaterMarkJson,
        string historyCompleteness,
        DateTime utcNow,
        string payloadStorageContract = "exact-text.v1")
    {
        EnsureLeaseOwner(owner, utcNow);
        if (string.IsNullOrWhiteSpace(historyCompleteness))
            throw new ArgumentException("History completeness is required.", nameof(historyCompleteness));
        if (string.IsNullOrWhiteSpace(CurrentStateConsistency)
            || string.Equals(CurrentStateConsistency, "UNKNOWN", StringComparison.Ordinal))
            throw new InvalidOperationException("A completed FULL_SOURCE generation must declare current-state consistency.");
        if (string.IsNullOrWhiteSpace(payloadStorageContract))
            throw new ArgumentException("Payload storage contract is required.", nameof(payloadStorageContract));
        HighWaterMarkJson = string.IsNullOrWhiteSpace(highWaterMarkJson) ? "{}" : highWaterMarkJson;
        HistoryCompleteness = historyCompleteness.Trim();
        PayloadStorageContract = payloadStorageContract.Trim();
        ContinuationToken = null;
        LastFullSourceCompletedAtUtc = utcNow;
        Generation = checked(Generation + 1);
        LastError = null;
        SetAuditTimestamps(utcNow);
    }

    public void MarkFailed(string owner, string error, DateTime utcNow)
    {
        EnsureLeaseOwner(owner, utcNow);
        LastError = string.IsNullOrWhiteSpace(error) ? "unknown capture error" : error.Trim();
        SetAuditTimestamps(utcNow);
    }

    public void ReleaseLease(string owner, DateTime utcNow)
    {
        if (!string.Equals(LeaseOwner, owner, StringComparison.Ordinal)) return;
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
        SetAuditTimestamps(utcNow);
    }

    private void EnsureLeaseOwner(string owner, DateTime utcNow)
    {
        if (!string.Equals(LeaseOwner, owner, StringComparison.Ordinal)
            || !LeaseExpiresAtUtc.HasValue || LeaseExpiresAtUtc.Value <= utcNow)
            throw new InvalidOperationException("Connector capture checkpoint is not leased by this worker.");
    }

    private static DateTime? Min(DateTime? left, DateTime? right)
        => left is null ? right : right is null ? left : left <= right ? left : right;
    private static DateTime? Max(DateTime? left, DateTime? right)
        => left is null ? right : right is null ? left : left >= right ? left : right;
}
