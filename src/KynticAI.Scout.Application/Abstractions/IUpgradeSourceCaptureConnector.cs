using System.Text.Json.Nodes;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Saas;

namespace KynticAI.Scout.Application.Abstractions;

/// <summary>
/// Optional connector contract for continuously capturing the complete customer-permitted
/// source stream into Scout's local source journal. This is deliberately separate from
/// IConnectorPlugin.FetchAsync: selector subject reads answer Scout questions, whereas this
/// contract preserves source truth so Fortress can later rebuild richer governed state without
/// reconnecting or silently losing records that Scout never happened to query.
/// </summary>
public interface IUpgradeSourceCaptureConnector
{
    string ConnectorType { get; }

    Task<ConnectorSourceCaptureBatch> CaptureBatchAsync(
        ConnectorSourceCaptureRequest request,
        CancellationToken cancellationToken);
}

public sealed record ConnectorSourceCaptureRequest(
    ConnectorInstallation Installation,
    DataSource DataSource,
    JsonObject Configuration,
    JsonObject Credentials,
    string? ContinuationToken,
    int MaxRecords,
    DateTime RequestedAtUtc);

public sealed record ConnectorSourceCaptureRecord(
    string SourceObjectType,
    string SourceRecordId,
    string Operation,
    string SourcePositionJson,
    DateTime OccurredAtUtc,
    DateTime? SourceRecordedAtUtc,
    string RawPayloadJson,
    JsonObject NormalizedPayload,
    string SchemaFingerprintSha256,
    string RawPayloadSha256,
    string PermittedFieldSetSha256,
    string RedactionPolicyVersion,
    string CaptureProfile,
    string CaptureProfileVersion,
    string HistoryCompleteness,
    DateTime? EarliestAvailableAtUtc,
    string IdempotencyKey);

/// <summary>
/// CurrentStateConsistency and HistoryCompleteness are batch-level guarantees as well as record
/// semantics. Keeping them on the batch is essential for a genuinely empty source: zero records
/// must still be able to prove that a complete enumeration occurred and what that enumeration
/// means, rather than being forced to invent a row merely to carry metadata.
/// </summary>
public sealed record ConnectorSourceCaptureBatch(
    IReadOnlyList<ConnectorSourceCaptureRecord> Records,
    string? NextContinuationToken,
    bool IsComplete,
    string HighWaterMarkJson,
    string DiagnosticsJson,
    string CurrentStateConsistency = "UNKNOWN",
    string HistoryCompleteness = "UNKNOWN");
