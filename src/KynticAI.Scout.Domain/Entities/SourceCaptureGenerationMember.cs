using KynticAI.Scout.Domain.Common;

namespace KynticAI.Scout.Domain.Entities;

/// <summary>
/// Records that a retained source event represented one source record in one completed/in-flight
/// FULL_SOURCE generation.
///
/// This is deliberately separate from SourceCapturePayloadEvidence. Exact payload evidence is
/// event-scoped and can be reused when an unchanged source record is observed in multiple
/// generations. Generation membership answers a different question: "was this source record
/// present in snapshot generation N?"
///
/// Without this table an old snapshot row can be replayed after the source record was deleted,
/// resurrecting stale current state during Scout -> Fortress upgrade.
/// </summary>
public sealed class SourceCaptureGenerationMember : AuditedTenantEntity
{
    private SourceCaptureGenerationMember() { }

    public Guid ConnectorInstallationId { get; private set; }

    public long Generation { get; private set; }

    public Guid SourceSystemEventId { get; private set; }

    public string SourceNamespace { get; private set; } = string.Empty;

    public string SourceObjectType { get; private set; } = string.Empty;

    public string SourceRecordId { get; private set; } = string.Empty;

    public SourceSystemEvent SourceSystemEvent { get; private set; } = null!;

    public static SourceCaptureGenerationMember Create(
        Guid tenantId,
        Guid connectorInstallationId,
        long generation,
        Guid sourceSystemEventId,
        string sourceNamespace,
        string sourceObjectType,
        string sourceRecordId,
        DateTime utcNow)
    {
        if (connectorInstallationId == Guid.Empty)
            throw new ArgumentException("Connector installation id is required.", nameof(connectorInstallationId));
        if (generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation), "Capture generation must be positive.");
        if (sourceSystemEventId == Guid.Empty)
            throw new ArgumentException("Source system event id is required.", nameof(sourceSystemEventId));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceObjectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRecordId);

        var member = new SourceCaptureGenerationMember
        {
            TenantId = tenantId,
            ConnectorInstallationId = connectorInstallationId,
            Generation = generation,
            SourceSystemEventId = sourceSystemEventId,
            SourceNamespace = sourceNamespace.Trim(),
            SourceObjectType = sourceObjectType.Trim(),
            SourceRecordId = sourceRecordId.Trim()
        };
        member.SetAuditTimestamps(utcNow);
        return member;
    }
}
