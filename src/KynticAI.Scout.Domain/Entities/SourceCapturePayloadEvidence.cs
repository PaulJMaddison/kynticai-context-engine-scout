using KynticAI.Scout.Domain.Common;

namespace KynticAI.Scout.Domain.Entities;

/// <summary>
/// Customer-local exact payload evidence for a retained SourceSystemEvent.
///
/// SourceSystemEvent.PayloadJson intentionally remains JSON/jsonb-friendly for Scout.
/// PostgreSQL jsonb may normalise representation, so it is not a byte-preserving evidence
/// store. This sidecar preserves the exact customer-permitted JSON text whose SHA-256 appears
/// in the capture metadata. It never leaves the customer data plane.
///
/// Evidence is deliberately event-scoped rather than generation-scoped. The same deterministic
/// source event may be encountered again during a later full-source generation; its exact bytes
/// remain evidence for that event without duplicating or relabelling history.
/// </summary>
public sealed class SourceCapturePayloadEvidence : AuditedTenantEntity
{
    private SourceCapturePayloadEvidence() { }

    public Guid SourceSystemEventId { get; private set; }

    public Guid ConnectorInstallationId { get; private set; }

    public string StorageContract { get; private set; } = string.Empty;

    public string CoverageScope { get; private set; } = string.Empty;

    public string ExactPayloadText { get; private set; } = string.Empty;

    public string RawPayloadSha256 { get; private set; } = string.Empty;

    public SourceSystemEvent SourceSystemEvent { get; private set; } = null!;

    public static SourceCapturePayloadEvidence Create(
        Guid tenantId,
        Guid sourceSystemEventId,
        Guid connectorInstallationId,
        string storageContract,
        string coverageScope,
        string exactPayloadText,
        string rawPayloadSha256,
        DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageContract);
        ArgumentException.ThrowIfNullOrWhiteSpace(coverageScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(exactPayloadText);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPayloadSha256);
        if (sourceSystemEventId == Guid.Empty)
            throw new ArgumentException("Source system event id is required.", nameof(sourceSystemEventId));
        if (connectorInstallationId == Guid.Empty)
            throw new ArgumentException("Connector installation id is required.", nameof(connectorInstallationId));
        if (rawPayloadSha256.Length != 64 || !rawPayloadSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Raw payload SHA-256 must be 64 hexadecimal characters.", nameof(rawPayloadSha256));

        var evidence = new SourceCapturePayloadEvidence
        {
            TenantId = tenantId,
            SourceSystemEventId = sourceSystemEventId,
            ConnectorInstallationId = connectorInstallationId,
            StorageContract = storageContract.Trim(),
            CoverageScope = coverageScope.Trim(),
            ExactPayloadText = exactPayloadText,
            RawPayloadSha256 = rawPayloadSha256.Trim().ToLowerInvariant()
        };
        evidence.SetAuditTimestamps(utcNow);
        return evidence;
    }
}
