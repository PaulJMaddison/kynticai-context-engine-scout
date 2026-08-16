using System.Text.Json.Nodes;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;

namespace KynticAI.Scout.Application.Abstractions;

public interface IConnectorPlugin
{
    string ConnectorType { get; }

    string DisplayName { get; }

    string Description { get; }

    IReadOnlyList<string> Aliases { get; }

    IReadOnlyList<DataSourceKind> SupportedDataSourceKinds { get; }

    IReadOnlyList<ConnectorCapability> SupportedCapabilities { get; }

    JsonObject GetConfigurationSchema();

    JsonObject GetCredentialSchema();

    JsonObject GetSampleConfiguration();

    Task<ConnectorConfigurationValidationResult> ValidateConfigurationAsync(
        ConnectorConfigurationValidationRequest request,
        CancellationToken cancellationToken);

    Task<ConnectorHealthCheckResult> CheckHealthAsync(
        ConnectorHealthCheckRequest request,
        CancellationToken cancellationToken);

    Task<ConnectorFetchResult> FetchAsync(
        ConnectorFetchRequest request,
        CancellationToken cancellationToken);
}

public interface IConnectorRegistry
{
    IReadOnlyList<IConnectorPlugin> GetPlugins();

    bool TryGetPlugin(string connectorType, out IConnectorPlugin plugin);

    IConnectorPlugin GetRequiredPlugin(string connectorType);
}

public interface IConnectorCredentialStore
{
    Task<JsonObject> PersistCredentialsAsync(
        Guid tenantId,
        Guid dataSourceId,
        string connectorType,
        JsonObject credentials,
        CancellationToken cancellationToken);

    Task<JsonObject> ResolveConfigurationSecretsAsync(
        Guid tenantId,
        JsonObject configuration,
        CancellationToken cancellationToken);
}

public enum ConnectorCapability
{
    FetchSubject = 1,
    Preview = 2,
    DryRun = 3,
    ScheduledSync = 4,
    EventTriggeredRecompute = 5,
    HealthCheck = 6,
    ConfigurationValidation = 7,
    SecureCredentialStorage = 8,
    UpgradeCompatibleCapture = 9
}

public enum ConnectorRunMode
{
    Live = 1,
    Preview = 2,
    DryRun = 3,
    ScheduledSync = 4,
    EventTriggeredRecompute = 5
}

public enum ConnectorConfigurationValueType
{
    String = 1,
    Number = 2,
    Integer = 3,
    Boolean = 4,
    Array = 5,
    Object = 6
}

public sealed record ConnectorConfigurationField(
    string Name,
    ConnectorConfigurationValueType Type,
    bool IsRequired,
    string Description,
    bool IsSecret = false,
    string? DefaultValueJson = null);

public sealed record ConnectorConfigurationDescriptor(
    string ConnectorType,
    string ConfigurationSchemaJson,
    string CredentialSchemaJson,
    string SampleConfigurationJson,
    IReadOnlyList<ConnectorConfigurationField> Fields);

public sealed record ConnectorEventShape(
    string SourceSystem,
    string EntityType,
    string SourceIdField,
    string? TimestampField,
    string? PayloadRoot);

public sealed record ConnectorIngestEvent(
    string SourceSystem,
    string SourceId,
    string EntityType,
    JsonObject RawPayload,
    DateTime TimestampUtc);

public sealed record ConnectorContractValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors);

public static class ConnectorContractRules
{
    public static ConnectorContractValidationResult ValidateIngestEvent(ConnectorIngestEvent ingestEvent)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ingestEvent.SourceSystem))
            errors.Add("Connector ingest event requires sourceSystem.");

        if (string.IsNullOrWhiteSpace(ingestEvent.SourceId))
            errors.Add("Connector ingest event requires sourceId.");

        if (string.IsNullOrWhiteSpace(ingestEvent.EntityType))
            errors.Add("Connector ingest event requires entityType.");

        if (ingestEvent.RawPayload.Count == 0)
            errors.Add("Connector ingest event requires a non-empty rawPayload object.");

        if (ingestEvent.TimestampUtc == default)
            errors.Add("Connector ingest event requires timestampUtc.");

        if (ingestEvent.TimestampUtc.Kind != DateTimeKind.Utc)
            errors.Add("Connector ingest event timestampUtc must be UTC.");

        return new ConnectorContractValidationResult(errors.Count == 0, errors);
    }
}

public sealed record ConnectorConfigurationValidationRequest(
    string ConnectorType,
    DataSourceKind DataSourceKind,
    JsonObject Configuration,
    JsonObject Credentials);

public sealed record ConnectorConfigurationValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    string SanitizedConfigurationJson,
    string SchemaJson);

public sealed record ConnectorHealthCheckRequest(
    string ConnectorType,
    DataSourceKind DataSourceKind,
    JsonObject Configuration,
    JsonObject Credentials,
    ConnectorSubject? Subject,
    ConnectorRunMode Mode);

public sealed record ConnectorHealthCheckResult(
    bool IsHealthy,
    string Status,
    IReadOnlyList<string> Messages,
    string DetailsJson,
    DateTime CheckedAtUtc);

public sealed record ConnectorFetchRequest(
    string ConnectorType,
    SelectorDefinition Selector,
    DataSource DataSource,
    UserProfile Subject,
    JsonObject Configuration,
    JsonObject Credentials,
    ConnectorRunMode Mode,
    ConnectorExecutionTrigger Trigger);

/// <summary>
/// Provider-native source position and capture semantics for continuity across KynticAI tiers.
/// SourcePositionJson is intentionally provider-specific and opaque to Scout; the target tier
/// validates it using connector type. No secret values belong in this metadata.
///
/// The optional fidelity fields default conservatively. A connector that wants to claim
/// FULL_SOURCE coverage or a historical retention boundary must set those values explicitly.
/// </summary>
public sealed record ConnectorCaptureMetadata(
    Guid ConnectorInstanceId,
    string ConnectorDefinitionVersion,
    string CaptureProfile,
    string CaptureProfileVersion,
    string? SourceNamespace,
    string SourceObjectType,
    string SourceRecordId,
    string Operation,
    string SourcePositionJson,
    DateTime OccurredAtUtc,
    DateTime? SourceRecordedAtUtc,
    string SchemaFingerprintSha256,
    string RedactionPolicyVersion,
    bool FullPermittedPayloadRetained,
    string IdempotencyKey,
    string CoverageScope = "SUBJECT_ON_DEMAND",
    string HistoryCompleteness = "UNKNOWN",
    DateTime? EarliestAvailableAtUtc = null,
    string RawPayloadSha256 = "",
    string PermittedFieldSetSha256 = "");

public sealed record ConnectorFetchResult(
    string RawPayloadJson,
    JsonObject NormalizedPayload,
    string ProvenanceJson,
    DateTime ObservedAtUtc,
    DateTime? FreshUntilUtc,
    string DiagnosticsJson,
    ConnectorCaptureMetadata? CaptureMetadata = null);

public sealed record ConnectorExecutionTrigger(
    string Kind,
    string? SourceEventId,
    string? TriggeredBy);

public sealed record ConnectorSubject(
    string ExternalUserId,
    string FullName,
    string Email,
    string CompanyName,
    string JobTitle,
    string Segment);
