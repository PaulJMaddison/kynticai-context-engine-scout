using System.Text.Json.Serialization;

namespace KynticAI.Scout.Application.Contracts;

/// <summary>
/// Public, product-neutral contract for preserving a connector capture so a later
/// KynticAI tier can rebuild richer local projections without reconnecting to the source.
/// It deliberately contains no credentials and no proprietary Fortress semantics.
/// </summary>
public static class LocalDataPlaneContracts
{
    public const string CaptureMetadataV1 = "kyntic-local-source-capture.v1";
    public const string UpgradeManifestV1 = "kyntic-scout-upgrade-manifest.v1";
    public const string CaptureProfileFullPermittedV1 = "full-permitted.v1";
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
/// complete source payload the customer has authorised this connector to collect, after
/// configured redaction/allow-list policy. It never means bypassing customer policy.
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
    string IdempotencyKey)
{
    public bool IsUpgradeCompatible =>
        string.Equals(Contract, LocalDataPlaneContracts.CaptureMetadataV1, StringComparison.Ordinal)
        && ConnectorInstanceId != Guid.Empty
        && !string.IsNullOrWhiteSpace(ConnectorType)
        && !string.IsNullOrWhiteSpace(CaptureProfileVersion)
        && !string.IsNullOrWhiteSpace(SourceObjectType)
        && !string.IsNullOrWhiteSpace(SourceRecordId)
        && !string.IsNullOrWhiteSpace(Operation)
        && !string.IsNullOrWhiteSpace(SourcePositionJson)
        && !string.IsNullOrWhiteSpace(SchemaFingerprintSha256)
        && FullPermittedPayloadRetained
        && !string.IsNullOrWhiteSpace(IdempotencyKey);
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
    IReadOnlyList<string> Warnings);

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

        // Scout's context facts/relationship fallback are derivatives. A Fortress upgrade
        // is expected to rebuild richer governed state locally from the retained source journal.
        return LocalUpgradeReadiness.LosslessDerivedRebuild;
    }
}
