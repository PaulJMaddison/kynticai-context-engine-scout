using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;

namespace KynticAI.Scout.Reference.Sales;

public sealed class BasicRelationshipEngine
{
    public const string EngineName = "BasicRelationshipEngine";
    public const string RelationshipWeightingScope = UclDataItemAttributionContractVersions.BasicFallbackOnlyScope;
    public const string CanonicalRelationshipWeightingOwner = "Enterprise";
    public const string CanonicalRelationshipWeightingEngine = "private relationship weighting/traversal engine";

    public UclRelationshipWeightingV1 BuildWeightingContract()
        => new(
            RelationshipWeightingScope,
            ScoutWeightsAreCanonical: false,
            CanonicalRelationshipWeightingOwner,
            CanonicalRelationshipWeightingEngine);

    public RelationshipResult BuildRelationship(
        string relationshipId,
        RelationshipType relationshipType,
        string linkKind,
        string sourceType,
        string sourceId,
        string targetType,
        string targetId,
        decimal confidence,
        string objective,
        string rationale,
        IReadOnlyList<string> citationIds)
    {
        var weight = ResolveFallbackWeight(relationshipType, objective);
        return new RelationshipResult(
            relationshipId,
            relationshipType.ToString(),
            linkKind,
            sourceType,
            sourceId,
            targetType,
            targetId,
            Math.Round(confidence, 4),
            weight.Weight,
            rationale,
            citationIds);
    }

    public RelationshipWeight ResolveFallbackWeight(RelationshipType type, string objective)
    {
        var normalizedObjective = NormalizeObjective(objective);
        var weight = type switch
        {
            RelationshipType.EmailToContact => 1.00m,
            RelationshipType.ContactToAccount => 1.00m,
            RelationshipType.AccountToOpportunity => normalizedObjective is "sale" or "conversion" ? 0.88m : 0.48m,
            RelationshipType.AccountToSalesActivity or RelationshipType.ContactToSalesActivity => normalizedObjective is "sale" or "conversion" ? 0.74m : 0.54m,
            RelationshipType.ContactToEmailEngagement => normalizedObjective is "sale" or "conversion" ? 0.78m : 0.44m,
            RelationshipType.AccountToWebConversion or RelationshipType.ContactToWebConversion => normalizedObjective is "sale" or "conversion" ? 0.80m : 0.42m,
            RelationshipType.AccountToSupportTicket or RelationshipType.ContactToSupportTicket => normalizedObjective is "support" or "churn" or "retention" ? 0.86m : 0.70m,
            RelationshipType.AccountToProductUsage or RelationshipType.ContactToProductUsage => 0.76m,
            RelationshipType.AccountToBilling => 0.70m,
            RelationshipType.AccountToOutcome or RelationshipType.ContactToOutcome => 0.92m,
            RelationshipType.SimilarSuccessfulSalePath => 0.82m,
            RelationshipType.SimilarProductUsagePattern => 0.72m,
            RelationshipType.SimilarWebJourney => 0.66m,
            RelationshipType.SimilarEmailResponsePattern => 0.64m,
            RelationshipType.SimilarSupportBlockers => 0.62m,
            RelationshipType.SameSegment => 0.48m,
            RelationshipType.SameRoleSeniority => 0.44m,
            RelationshipType.SameDomain => 0.38m,
            _ => 0.50m
        };

        return new RelationshipWeight(type, normalizedObjective, weight, "fallback", $"Basic fallback-only public weight for {type} under {normalizedObjective} objective.");
    }

    private static string NormalizeObjective(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "sales" => "sale",
            "sell" => "sale",
            "conversion" => "conversion",
            "convert" => "conversion",
            "churn" => "churn",
            "support" => "support",
            "retain" => "retention",
            "retention" => "retention",
            "sale" => "sale",
            _ => value.Trim().ToLowerInvariant()
        };
}
