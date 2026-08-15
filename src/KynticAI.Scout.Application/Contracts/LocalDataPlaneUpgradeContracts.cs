using System.Text.Json.Serialization;

namespace KynticAI.Scout.Application.Contracts;

/// <summary>
/// Public, product-neutral contracts for retaining source capture locally so a later
/// KynticAI tier can rebuild richer governed projections without reconnecting to the source.
/// These contracts describe capture fidelity; they never grant permission to collect fields
/// the customer has not authorised.
/// </summary>
public static class LocalDataPlaneContracts
{
    public const string CaptureMetadataV1 = "kyntic-local-source-capture.v1";
    public const string UpgradeManifestV1 = "kyntic-scout-upgrade-manifest.v1";
    public const string CaptureProfileFullPermittedV1 = "full-permitted.v1";

    public const string CoverageFullSource = "FULL_SOURCE";
    public const string CoverageSubjectOnDemand = "SUBJECT_ON_DEMAND";
    public const string CoverageSnapshotImport = "SNAPSHOT_IMPORT";

    public const string HistoryComplete = "COMPLETE";
    public const string HistoryFromRetentionBoundary = "FROM_RETENTION_BOUNDARY";
    public const string HistoryOnDemand = "ON_DEMAND";
    public const string HistorySnapshotOnly = "SNAPSHOT_ONLY";
    public const string HistoryUnknown = "UNKNOWN";
}

public enum LocalUpgradeReadiness
{
    Lossless = 1,
    LosslessDerivedRebuild = 2,
    HistoryLimited = 3,
    ReconnectRequired = 4,
    Unsupported = 5
}

/// <summary>
/// Metadata attached to a locally retained source event. "Full permitted" means the
/// complete source payload the customer authorised this connector to collect, after the
/// configured redaction/allow-list policy. It does not mean "all fields in the source".
///
/// CoverageScope is deliberately separate from FullPermittedPayloadRetained. A subject
/// lookup can retain its complete permitted payload and still not prove that Scout has
/// captured the whole source estate. Lossless Scout -> Fortress claims require FULL_SOURCE
/// coverage plus an explicit history completeness statement.
/// </summary>
public sealed record LocalSourceCaptureMetadataV1(
    string Contract,
    Guid ConnectorInstanceId,
    string ConnectorType,
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
    DateTime IngestedAtUtc,
    string SchemaFingerprintSha256,
    string RedactionPolicyVersion,
    bool FullPermittedPayloadRetained,
    string IdempotencyKey,
    string CoverageScope = LocalDataPlaneContracts.CoverageSubjectOnDemand,
    string HistoryCompleteness = LocalDataPlaneContracts.HistoryUnknown,
    DateTime? EarliestAvailableAtUtc = null,
    string RawPayloadSha256 = "",
    string PermittedFieldSetSha256 = "")
{
    [JsonIgnore]
    public bool HasStructurallyValidCaptureMetadata =>
        string.Equals(Contract, LocalDataPlaneContracts.CaptureMetadataV1, StringComparison.Ordinal)
        && ConnectorInstanceId != Guid.Empty
        && !string.IsNullOrWhiteSpace(ConnectorType)
        && !string.IsNullOrWhiteSpace(CaptureProfileVersion)
        && !string.IsNullOrWhiteSpace(SourceObjectType)
        && !string.IsNullOrWhiteSpace(SourceRecordId)
        && !string.IsNullOrWhiteSpace(Operation)
        && !string.IsNullOrWhiteSpace(SourcePositionJson)
        && !string.IsNullOrWhiteSpace(SchemaFingerprintSha256)
        && !string.IsNullOrWhiteSpace(IdempotencyKey)
        && !string.IsNullOrWhiteSpace(CoverageScope)
        && !string.IsNullOrWhiteSpace(HistoryCompleteness)
        && !string.IsNullOrWhiteSpace(RawPayloadSha256);

    [JsonIgnore]
    public bool IsUpgradeCompatible =>
        HasStructurallyValidCaptureMetadata
        && FullPermittedPayloadRetained
        && string.Equals(CoverageScope, LocalDataPlaneContracts.CoverageFullSource, StringComparison.Ordinal)
        && (string.Equals(HistoryCompleteness, LocalDataPlaneContracts.HistoryComplete, StringComparison.Ordinal)
            || string.Equals(HistoryCompleteness, LocalDataPlaneContracts.HistoryFromRetentionBoundary, StringComparison.Ordinal));
}

public sealed record ScoutConnectorUpgradeDescriptorV1(
    Guid ConnectorInstanceId,
    Guid DataSourceId,
    Guid WorkspaceId,
    string ConnectorType,
    string Status,
    string ConfigurationSha256,
    IReadOnlyList<string> CredentialReferences,
    long RetainedEventCount,
    DateTime? EarliestRetainedObservedAtUtc,
    DateTime? LatestRetainedObservedAtUtc,
    bool HasUpgradeCaptureMetadata,
    bool AllRetainedEventsHaveUpgradeCaptureMetadata,
    bool FullPermittedPayloadRetained,
    IReadOnlyList<string> CaptureProfiles,
    IReadOnlyList<string> SchemaFingerprints,
    IReadOnlyList<string> Warnings,
    string CoverageScope = LocalDataPlaneContracts.HistoryUnknown,
    string HistoryCompleteness = LocalDataPlaneContracts.HistoryUnknown,
    DateTime? EarliestUpgradeCompatibleAtUtc = null,
    DateTime? LastFullSourceCompletedAtUtc = null,
    long CompletedCaptureGeneration = 0,
    string HighWaterMarkSha256 = "");

public sealed record ScoutUpgradeManifestV1(
    string Contract,
    Guid TenantId,
    string TenantSlug,
    DateTime GeneratedAtUtc,
    string StorageProvider,
    bool CustomerDataRemainsLocal,
    bool ContainsCredentials,
    long RetainedSourceEventCount,
    DateTime? EarliestRetainedObservedAtUtc,
    DateTime? LatestRetainedObservedAtUtc,
    IReadOnlyList<ScoutConnectorUpgradeDescriptorV1> Connectors,
    LocalUpgradeReadiness Readiness,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> RequiredActions,
    string DataBoundary)
{
    [JsonIgnore]
    public bool IsSafeForControlPlane => CustomerDataRemainsLocal && !ContainsCredentials;
}

public sealed record UpgradeCompatibilityEvidence(
    bool SupportedRelationalProvider,
    bool IsPostgres,
    bool HasConnectorInstallations,
    bool ConnectorCredentialsReferencedLocally,
    bool HasRetainedEvents,
    bool AllRetainedEventsHaveCaptureMetadata,
    bool AllRetainedEventsRetainFullPermittedPayload,
    bool ConnectorTypesSupportedByTarget,
    bool RequiresSourceReconnect,
    bool HistoricalCoverageKnownComplete);

public static class ScoutFortressUpgradePolicy
{
    public static LocalUpgradeReadiness Classify(UpgradeCompatibilityEvidence evidence)
    {
        if (!evidence.SupportedRelationalProvider || !evidence.ConnectorTypesSupportedByTarget)
            return LocalUpgradeReadiness.Unsupported;

        if (evidence.RequiresSourceReconnect || !evidence.HasConnectorInstallations)
            return LocalUpgradeReadiness.ReconnectRequired;

        if (!evidence.HasRetainedEvents)
            return evidence.IsPostgres
                ? LocalUpgradeReadiness.LosslessDerivedRebuild
                : LocalUpgradeReadiness.ReconnectRequired;

        if (!evidence.AllRetainedEventsHaveCaptureMetadata
            || !evidence.AllRetainedEventsRetainFullPermittedPayload
            || !evidence.HistoricalCoverageKnownComplete)
            return LocalUpgradeReadiness.HistoryLimited;

        if (!evidence.ConnectorCredentialsReferencedLocally)
            return LocalUpgradeReadiness.ReconnectRequired;

        return LocalUpgradeReadiness.LosslessDerivedRebuild;
    }
}
