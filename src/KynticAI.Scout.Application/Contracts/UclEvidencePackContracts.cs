namespace KynticAI.Scout.Application.Contracts;

public static class UclEvidencePackContractVersions
{
    public const string EvidencePackKind = "ucl.evidence-pack";
    public const string EvidencePackV1 = "ucl.evidence-pack.v1";
    public const string CloudAggregateUsageKind = "cloud-aggregate-usage";
    public const string CloudAggregateUsageV1 = "ucl.cloud-aggregate-usage.v1";
    public const string EnterpriseRelationshipEngineHandoffKind = "ucl.enterprise-relationship-engine-handoff";
    public const string EnterpriseRelationshipEngineHandoffV1 = "ucl.enterprise-relationship-engine-handoff.v1";
    public const string ScoutLocalContextBriefKind = "scout-local-context-brief";
    public const string ScoutLocalContextBriefV1 = "context-engine.scout-local-context-brief.v1";
    public const string CustomerOwnedDataPlane = "customer-owned-data-plane";
}

public sealed record UclEvidencePackV1(
    string PackageKind,
    string PackageVersion,
    string PackageId,
    DateTime GeneratedAtUtc,
    string TenantSlug,
    string DataPlane,
    UclEvidenceSubjectV1 Subject,
    string Objective,
    string Purpose,
    string ActorRole,
    ExactLinkedRecordsSummaryResult ExactLinkedRecords,
    IReadOnlyList<RelationshipResult> Relationships,
    IReadOnlyList<SimilarPatternMatchResult> SimilarWonLostPatterns,
    IReadOnlyList<WeightedSignalResult> WeightedSignals,
    UclRelationshipWeightingV1 RelationshipWeighting,
    RecommendedNextActionResult RecommendedAction,
    DraftResponseResult? DraftResponse,
    decimal Confidence,
    IReadOnlyList<string> Caveats,
    IReadOnlyList<ProvenanceCitationResult> Provenance,
    UclEvidenceGovernanceV1 Governance);

public sealed record UclEvidenceSubjectV1(
    string SubjectType,
    string SubjectIdentifier,
    string ExternalAccountId,
    string? PrimaryContactId);

public sealed record UclEvidenceGovernanceV1(
    IReadOnlyList<string> AppliedRules,
    IReadOnlyList<string> MaskedFields,
    IReadOnlyList<string> DeniedFields,
    bool RawDataRetainedInCustomerDataPlane);

public sealed record UclRelationshipWeightingV1(
    string Scope,
    bool ScoutWeightsAreCanonical,
    string CanonicalOwner,
    string CanonicalEngine);

public sealed record UclCloudAggregateUsageV1(
    string PayloadKind,
    string PayloadVersion,
    string PackageVersion,
    string TenantSlug,
    string Feature,
    string EventName,
    string Status,
    DateTime GeneratedAtUtc,
    UclCloudFeatureUsageCountersV1 FeatureUsageCounters,
    UclCloudControlPlaneCountersV1 ControlPlaneCounters,
    UclCloudDataBoundaryV1 DataBoundary);

public sealed record UclCloudFeatureUsageCountersV1(
    int NextActionGenerateRequests,
    int DataPlanePackageBuilds);

public sealed record UclCloudControlPlaneCountersV1(
    int AppliedRuleCount,
    int MaskedFieldCount,
    int DeniedFieldCount);

public sealed record UclCloudDataBoundaryV1(
    bool RawDataRetainedInCustomerDataPlane,
    bool ContainsRawCustomerData,
    bool ContainsRecords,
    bool ContainsFacts,
    bool ContainsContextFacts,
    bool ContainsSnapshots,
    bool ContainsContextSnapshots,
    bool ContainsEvidencePacks,
    bool ContainsPrompts,
    bool ContainsGeneratedContent,
    bool ContainsRecommendations,
    bool ContainsCitations,
    bool ContainsCitationIds,
    bool ContainsRelationshipTypes,
    bool ContainsWeightedSignals,
    bool ContainsCaveats,
    bool ContainsPerEntityRelationshipMetadata,
    bool ContainsDerivedRelationshipIntelligence,
    bool ContainsPerCustomerDerivedIntelligence);

public sealed record UclEnterpriseRelationshipEngineHandoffV1(
    string ArtifactKind,
    string ArtifactVersion,
    string HandoffId,
    string PackageKind,
    string PackageVersion,
    string PackageId,
    DateTime GeneratedAtUtc,
    string TenantSlug,
    string DataPlane,
    string Producer,
    string FallbackEngine,
    UclRelationshipWeightingV1 RelationshipWeighting,
    bool RequiresLiveEnterpriseService,
    bool EnterpriseOnlyInternalsIncluded,
    UclEvidenceSubjectV1 Subject,
    string Objective,
    string Purpose,
    string ActorRole,
    UclEnterpriseRelationshipHandoffEvidenceSummaryV1 EvidenceSummary,
    IReadOnlyList<UclEnterpriseRelationshipHandoffCandidateV1> CandidateRelationships,
    IReadOnlyList<ProvenanceCitationResult> Provenance,
    IReadOnlyList<string> RequiredEnterpriseOutputs);

public sealed record UclEnterpriseRelationshipHandoffEvidenceSummaryV1(
    IReadOnlyDictionary<string, int> RecordCounts,
    int ExactRecordCount,
    int CandidateRelationshipCount,
    int ProvenanceCitationCount);

public sealed record UclEnterpriseRelationshipHandoffCandidateV1(
    string RelationshipId,
    string RelationshipType,
    string LinkKind,
    string SourceType,
    string SourceId,
    string TargetType,
    string TargetId,
    decimal Confidence,
    decimal ScoutFallbackWeight,
    string FallbackWeightScope,
    string Rationale,
    IReadOnlyList<string> CitationIds);
