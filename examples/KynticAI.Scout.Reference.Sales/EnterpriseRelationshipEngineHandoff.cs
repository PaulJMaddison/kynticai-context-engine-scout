using KynticAI.Scout.Application.Contracts;

namespace KynticAI.Scout.Reference.Sales;

public sealed class EnterpriseRelationshipEngineHandoff
{
    public const string Producer = "KynticAI Scout";
    public const string CanonicalConsumer = "Enterprise canonical relationship weighting proof runner";

    public UclEnterpriseRelationshipEngineHandoffV1 BuildArtifact(UclEvidencePackV1 package)
        => new(
            UclEvidencePackContractVersions.EnterpriseRelationshipEngineHandoffKind,
            UclEvidencePackContractVersions.EnterpriseRelationshipEngineHandoffV1,
            $"ERH-{Guid.NewGuid():N}",
            package.PackageKind,
            package.PackageVersion,
            package.PackageId,
            package.GeneratedAtUtc,
            package.TenantSlug,
            package.DataPlane,
            Producer,
            BasicRelationshipEngine.EngineName,
            package.RelationshipWeighting,
            RequiresLiveEnterpriseService: false,
            EnterpriseOnlyInternalsIncluded: false,
            package.Subject,
            package.Objective,
            package.Purpose,
            package.ActorRole,
            new UclEnterpriseRelationshipHandoffEvidenceSummaryV1(
                package.ExactLinkedRecords.RecordCounts,
                package.ExactLinkedRecords.Records.Count,
                package.Relationships.Count,
                package.Provenance.Count),
            package.Relationships
                .Select(relationship => new UclEnterpriseRelationshipHandoffCandidateV1(
                    relationship.RelationshipId,
                    relationship.RelationshipType,
                    relationship.LinkKind,
                    relationship.SourceType,
                    relationship.SourceId,
                    relationship.TargetType,
                    relationship.TargetId,
                    relationship.Confidence,
                    relationship.Weight,
                    package.RelationshipWeighting.Scope,
                    relationship.Rationale,
                    relationship.CitationIds))
                .ToList(),
            package.Provenance,
            [
                "canonicalRelationshipWeights",
                "canonicalTraversalSignals",
                "canonicalWeightingDecisionTrace"
            ]);
}
