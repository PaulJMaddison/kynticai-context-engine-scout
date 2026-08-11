using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Application.Validation;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.UnitTests;

public sealed class NextActionIntelligenceServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SameEmail_LinksToContactAccountAndHistory()
    {
        await using var harness = await NextActionHarness.CreateAsync();

        var result = await harness.Service.GenerateNextActionAsync(SaleRequest("email", "avery@example.test"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.ExactLinkedRecords.Records, x => x.RecordType == "CustomerContact" && x.Fields["email"] == "avery@example.test");
        Assert.Contains(result.ExactLinkedRecords.Records, x => x.RecordType == "CustomerAccount" && x.ExternalId == "acct-subject");
        Assert.Contains(result.ExactLinkedRecords.Records, x => x.RecordType == "SalesActivity");
        Assert.Contains(result.ExactLinkedRecords.Records, x => x.RecordType == "EmailEngagementEvent");
        Assert.Contains(result.ExactLinkedRecords.Records, x => x.RecordType == "WebConversionEvent");
        Assert.Contains(result.ExactLinkedRecords.Records, x => x.RecordType == "SupportTicket");
        Assert.Contains(result.ExactLinkedRecords.Records, x => x.RecordType == "ProductUsageSummary");
        Assert.Contains(result.ExactLinkedRecords.Records, x => x.RecordType == "BillingMetric");
        Assert.Contains(result.ExactLinkedRecords.Records, x => x.RecordType == "OutcomeSignal");
        Assert.Contains(result.Relationships, x => x.RelationshipType == "EmailToContact" && x.LinkKind == "deterministic");
        Assert.Contains(result.Relationships, x => x.RelationshipType == "ContactToAccount" && x.LinkKind == "deterministic");
        Assert.Contains(result.Relationships, x => x.RelationshipType == "AccountToOpportunity" && x.CitationIds.Count > 0);
        Assert.Contains(result.Relationships, x => x.RelationshipType == "AccountToSalesActivity" && x.LinkKind == "deterministic");
        Assert.Contains(result.Relationships, x => x.RelationshipType == "ContactToSalesActivity" && x.LinkKind == "deterministic");
        Assert.Contains(result.Relationships, x => x.RelationshipType == "ContactToEmailEngagement" && x.LinkKind == "deterministic");
        Assert.Contains(result.Relationships, x => x.RelationshipType == "AccountToWebConversion" && x.LinkKind == "deterministic");
        Assert.Contains(result.Relationships, x => x.RelationshipType == "ContactToWebConversion" && x.LinkKind == "deterministic");
        Assert.Contains(result.Relationships, x => x.RelationshipType == "AccountToSupportTicket" && x.LinkKind == "deterministic");
        Assert.Contains(result.Relationships, x => x.RelationshipType == "ContactToSupportTicket" && x.LinkKind == "deterministic");
        Assert.Contains(result.Relationships, x => x.RelationshipType == "AccountToProductUsage" && x.LinkKind == "deterministic");
        Assert.Contains(result.Relationships, x => x.RelationshipType == "ContactToProductUsage" && x.LinkKind == "deterministic");
        Assert.Contains(result.Relationships, x => x.RelationshipType == "AccountToBilling" && x.LinkKind == "deterministic");
        Assert.Contains(result.Relationships, x => x.RelationshipType == "AccountToOutcome" && x.LinkKind == "deterministic");
        Assert.Contains(result.Relationships, x => x.RelationshipType == "ContactToOutcome" && x.LinkKind == "deterministic");
    }

    [Fact]
    public async Task SimilarSuccessfulContacts_AreFoundFromWonOutcomes()
    {
        await using var harness = await NextActionHarness.CreateAsync();

        var result = await harness.Service.GenerateNextActionAsync(SaleRequest("contact", "contact-subject"), CancellationToken.None);

        Assert.NotNull(result);
        var wonPattern = Assert.Single(result.SimilarWonLostPatterns, x => x.Outcome == "won");
        Assert.True(wonPattern.SimilarityScore >= 0.45m);
        Assert.Contains("SameSegment", wonPattern.RelationshipTypes);
        Assert.Contains(result.Relationships, x => x.LinkKind == "probabilistic" && x.RelationshipType == "SimilarSuccessfulSalePath");
    }

    [Fact]
    public async Task SupportAndBillingBlockers_ReduceConfidence()
    {
        await using var cleanHarness = await NextActionHarness.CreateAsync();
        await using var blockedHarness = await NextActionHarness.CreateAsync(openSupportTickets: 2, daysPastDue: 21, paymentFailures30d: 2);

        var clean = await cleanHarness.Service.GenerateNextActionAsync(SaleRequest("email", "avery@example.test"), CancellationToken.None);
        var blocked = await blockedHarness.Service.GenerateNextActionAsync(SaleRequest("email", "avery@example.test"), CancellationToken.None);

        Assert.NotNull(clean);
        Assert.NotNull(blocked);
        Assert.True(blocked.Confidence < clean.Confidence);
        Assert.True(blocked.RecommendedNextAction.Score < clean.RecommendedNextAction.Score);
        Assert.Contains(blocked.WeightedSignals, x => x.SignalKey == "support-blockers" && x.Contribution < 0m);
        Assert.Contains(blocked.WeightedSignals, x => x.SignalKey == "billing-blockers" && x.Contribution < 0m);
    }

    [Fact]
    public async Task PricingVisitsEmailRepliesAndActiveUsage_IncreaseSalesRecommendationScore()
    {
        await using var lowIntentHarness = await NextActionHarness.CreateAsync(
            pricingVisits30d: 0,
            emailReplies30d: 0,
            activeDays30: 4,
            featureAdoptionScore: 22,
            openOpportunityProbability: 20);
        await using var highIntentHarness = await NextActionHarness.CreateAsync(
            pricingVisits30d: 9,
            emailReplies30d: 3,
            activeDays30: 26,
            featureAdoptionScore: 91,
            openOpportunityProbability: 82);

        var lowIntent = await lowIntentHarness.Service.GenerateNextActionAsync(SaleRequest("email", "avery@example.test"), CancellationToken.None);
        var highIntent = await highIntentHarness.Service.GenerateNextActionAsync(SaleRequest("email", "avery@example.test"), CancellationToken.None);

        Assert.NotNull(lowIntent);
        Assert.NotNull(highIntent);
        Assert.True(highIntent.RecommendedNextAction.Score > lowIntent.RecommendedNextAction.Score);
        Assert.Contains(highIntent.WeightedSignals, x => x.SignalKey == "pricing-intent" && x.Score > 0.9m);
        Assert.Contains(highIntent.WeightedSignals, x => x.SignalKey == "email-response" && x.Score >= 1m);
        Assert.Contains(highIntent.WeightedSignals, x => x.SignalKey == "active-usage" && x.Score > 0.75m);
    }

    [Fact]
    public void BasicRelationshipEngine_ProducesLocalFallbackWeightsAndEnterpriseOwnershipMetadata()
    {
        var engine = new BasicRelationshipEngine();

        var relationship = engine.BuildRelationship(
            "REL-01",
            RelationshipType.AccountToOpportunity,
            "deterministic",
            "CustomerAccount",
            "account-1",
            "SalesOpportunity",
            "opportunity-1",
            1.0m,
            "sale",
            "Opportunity carries the account foreign key.",
            ["EVID-01"]);
        var weighting = engine.BuildWeightingContract();

        Assert.Equal("AccountToOpportunity", relationship.RelationshipType);
        Assert.Equal("deterministic", relationship.LinkKind);
        Assert.Equal(0.88m, relationship.Weight);
        Assert.Equal("basic-public-fallback-only", weighting.Scope);
        Assert.False(weighting.ScoutWeightsAreCanonical);
        Assert.Equal("Enterprise", weighting.CanonicalOwner);
        Assert.Contains("private relationship weighting", weighting.CanonicalEngine, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(FallbackWeightCases))]
    public void BasicRelationshipEngine_ResolvesObjectiveAliasesAndUnknownObjectivesToDeterministicFallbackWeights(
        RelationshipType relationshipType,
        string objective,
        string expectedNormalisedObjective,
        decimal expectedWeight)
    {
        var engine = new BasicRelationshipEngine();

        var result = engine.ResolveFallbackWeight(relationshipType, objective);

        Assert.Equal(expectedNormalisedObjective, result.Objective);
        Assert.Equal(expectedWeight, result.Weight);
        Assert.Equal("fallback", result.Direction);
        Assert.Contains("fallback-only public weight", result.Rationale, StringComparison.Ordinal);
    }

    public static TheoryData<RelationshipType, string, string, decimal> FallbackWeightCases => new()
    {
        { RelationshipType.EmailToContact, "unknown", "unknown", 1.00m },
        { RelationshipType.ContactToAccount, "retention", "retention", 1.00m },
        { RelationshipType.AccountToOpportunity, "sales", "sale", 0.88m },
        { RelationshipType.AccountToOpportunity, "conversion", "conversion", 0.88m },
        { RelationshipType.AccountToOpportunity, "renewal", "renewal", 0.48m },
        { RelationshipType.AccountToSalesActivity, "sell", "sale", 0.74m },
        { RelationshipType.ContactToSalesActivity, "convert", "conversion", 0.74m },
        { RelationshipType.ContactToEmailEngagement, "sale", "sale", 0.78m },
        { RelationshipType.ContactToEmailEngagement, "support", "support", 0.44m },
        { RelationshipType.AccountToWebConversion, "convert", "conversion", 0.80m },
        { RelationshipType.ContactToWebConversion, "retention", "retention", 0.42m },
        { RelationshipType.AccountToSupportTicket, "support", "support", 0.86m },
        { RelationshipType.ContactToSupportTicket, "retain", "retention", 0.86m },
        { RelationshipType.AccountToSupportTicket, "sale", "sale", 0.70m },
        { RelationshipType.AccountToProductUsage, "unknown", "unknown", 0.76m },
        { RelationshipType.ContactToProductUsage, "conversion", "conversion", 0.76m },
        { RelationshipType.AccountToBilling, "churn", "churn", 0.70m },
        { RelationshipType.AccountToOutcome, "unknown", "unknown", 0.92m },
        { RelationshipType.ContactToOutcome, "sale", "sale", 0.92m },
        { RelationshipType.SimilarSuccessfulSalePath, "unknown", "unknown", 0.82m },
        { RelationshipType.SimilarProductUsagePattern, "unknown", "unknown", 0.72m },
        { RelationshipType.SimilarWebJourney, "unknown", "unknown", 0.66m },
        { RelationshipType.SimilarEmailResponsePattern, "unknown", "unknown", 0.64m },
        { RelationshipType.SimilarSupportBlockers, "unknown", "unknown", 0.62m },
        { RelationshipType.SameSegment, "unknown", "unknown", 0.48m },
        { RelationshipType.SameRoleSeniority, "unknown", "unknown", 0.44m },
        { RelationshipType.SameDomain, "unknown", "unknown", 0.38m },
        { (RelationshipType)999, "unrecognised-objective", "unrecognised-objective", 0.50m }
    };

    [Fact]
    public async Task ReadOnlyActors_ReceiveMaskedFields()
    {
        await using var harness = await NextActionHarness.CreateAsync(role: OperatorRole.ReadOnly);

        var result = await harness.Service.GenerateNextActionAsync(SaleRequest("email", "avery@example.test", actorRole: "read_only"), CancellationToken.None);

        Assert.NotNull(result);
        var contact = Assert.Single(result.ExactLinkedRecords.Records, x => x.RecordType == "CustomerContact" && x.ExternalId == "contact-subject");
        Assert.Equal("a***@example.test", contact.Fields["email"]);
        Assert.Contains("contact.email", result.Governance.MaskedFields);
        Assert.Contains("account.name", result.Governance.MaskedFields);
        Assert.DoesNotContain("avery@example.test", result.EvidencePack.LocalDerivedEvidencePackageJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GeneratedLocalEvidencePackV1_ContainsExactCitationsAndProvenance()
    {
        await using var harness = await NextActionHarness.CreateAsync();

        var result = await harness.Service.GenerateNextActionAsync(SaleRequest("email", "avery@example.test"), CancellationToken.None);

        Assert.NotNull(result);
        var package = JsonSerializer.Deserialize<UclEvidencePackV1>(
            result.EvidencePack.LocalDerivedEvidencePackageJson,
            JsonOptions);
        Assert.NotNull(package);
        var validation = UclEvidencePackV1Validator.Validate(package);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.Equal("ucl.evidence-pack", package.PackageKind);
        Assert.Equal("ucl.evidence-pack.v1", package.PackageVersion);
        Assert.Equal("customer-owned-data-plane", package.DataPlane);
        Assert.False(package.RelationshipWeighting.ScoutWeightsAreCanonical);
        Assert.Equal("Enterprise", package.RelationshipWeighting.CanonicalOwner);

        foreach (var record in package.ExactLinkedRecords.Records)
        {
            Assert.Contains(package.Provenance, x =>
                x.CitationId == record.CitationId
                && x.SourceEntityType == record.RecordType
                && x.SourceEntityId == record.RecordId);
        }

        var provenanceIds = package.Provenance.Select(x => x.CitationId).ToHashSet(StringComparer.Ordinal);
        foreach (var citationId in package.RecommendedAction.CitationIds)
        {
            Assert.Contains(citationId, provenanceIds);
        }
    }

    [Fact]
    public async Task CloudBoundOutputs_DoNotContainRawDataOrDerivedIntelligence()
    {
        await using var harness = await NextActionHarness.CreateAsync(openSupportTickets: 1, daysPastDue: 9);

        var result = await harness.Service.GenerateNextActionAsync(
            SaleRequest("email", "avery@example.test", purpose: "Avery-Acme-raw-purpose-7781"),
            CancellationToken.None);

        Assert.NotNull(result);
        var cloudPayload = result.EvidencePack.CloudAggregateUsagePayloadJson;
        var cloudJson = JsonNode.Parse(cloudPayload)!.AsObject();
        var dataBoundary = cloudJson["dataBoundary"]!.AsObject();
        var cloudValidation = UclCloudAggregateUsageV1Validator.ValidateJson(cloudPayload);
        Assert.True(cloudValidation.IsValid, string.Join(Environment.NewLine, cloudValidation.Errors));
        Assert.False(result.EvidencePack.CloudPayloadContainsRawCustomerData);
        Assert.Equal(result.EvidencePack.CloudAggregateUsagePayloadJson, result.Governance.CloudAggregateUsagePayloadJson);
        Assert.Equal("cloud-aggregate-usage", cloudJson["payloadKind"]!.GetValue<string>());
        Assert.Equal("next-action", cloudJson["feature"]!.GetValue<string>());
        Assert.Equal("succeeded", cloudJson["status"]!.GetValue<string>());
        Assert.NotNull(cloudJson["featureUsageCounters"]);
        Assert.False(dataBoundary["containsRawCustomerData"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsRecords"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsFacts"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsContextFacts"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsSnapshots"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsContextSnapshots"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsEvidencePacks"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsPrompts"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsGeneratedContent"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsRecommendations"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsCitations"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsCitationIds"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsRelationshipTypes"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsWeightedSignals"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsCaveats"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsPerEntityRelationshipMetadata"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsDerivedRelationshipIntelligence"]!.GetValue<bool>());
        Assert.False(dataBoundary["containsPerCustomerDerivedIntelligence"]!.GetValue<bool>());
        Assert.Null(cloudJson["subject"]);
        Assert.Null(cloudJson["objective"]);
        Assert.Null(cloudJson["purposeCategory"]);
        Assert.Null(cloudJson["purposeHash"]);
        Assert.Null(cloudJson["actorRole"]);
        Assert.Null(cloudJson["exactRecordCounts"]);
        Assert.Null(cloudJson["relationshipTypes"]);
        Assert.Null(cloudJson["weightedSignals"]);
        Assert.Null(cloudJson["recommendation"]);
        Assert.Null(cloudJson["confidence"]);
        Assert.Null(cloudJson["caveats"]);
        Assert.Null(cloudJson["citationIds"]);
        Assert.Null(cloudJson["governance"]);
        Assert.DoesNotContain("Avery", cloudPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("avery@example.test", cloudPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Acme Corp", cloudPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acme.example", cloudPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw-purpose-7781", cloudPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Payment blocker blocks rollout", cloudPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EmailToContact", cloudPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("ContactToAccount", cloudPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("pricing-intent", cloudPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("support-blockers", cloudPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("billing-blockers", cloudPayload, StringComparison.Ordinal);
        Assert.DoesNotContain(result.RecommendedNextAction.Action, cloudPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EVID-", cloudPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("PAT-", cloudPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("\"records\"", cloudPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"fields\"", cloudPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"summary\"", cloudPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"relationshipTypes\"", cloudPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"weightedSignals\"", cloudPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"citationIds\"", cloudPayload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Avery", result.EvidencePack.LocalDerivedEvidencePackageJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"relationships\"", result.EvidencePack.LocalDerivedEvidencePackageJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"weightedSignals\"", result.EvidencePack.LocalDerivedEvidencePackageJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"recommendedAction\"", result.EvidencePack.LocalDerivedEvidencePackageJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"citationId\"", result.EvidencePack.LocalDerivedEvidencePackageJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocalRelationshipWeights_AreDeclaredAsScoutFallbackNotCanonicalEnterpriseWeights()
    {
        await using var harness = await NextActionHarness.CreateAsync();

        var result = await harness.Service.GenerateNextActionAsync(
            SaleRequest("email", "avery@example.test"),
            CancellationToken.None);

        Assert.NotNull(result);
        var localPackage = result.EvidencePack.LocalDerivedEvidencePackageJson;
        var localJson = JsonNode.Parse(localPackage)!.AsObject();
        var ownership = localJson["relationshipWeighting"]!.AsObject();

        Assert.Equal("basic-public-fallback-only", ownership["scope"]!.GetValue<string>());
        Assert.False(ownership["scoutWeightsAreCanonical"]!.GetValue<bool>());
        Assert.Equal("Enterprise", ownership["canonicalOwner"]!.GetValue<string>());
        Assert.Equal(
            "private relationship weighting/traversal engine",
            ownership["canonicalEngine"]!.GetValue<string>());
        Assert.DoesNotContain("UCL owns canonical relationship weighting", localPackage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Scout canonical weighting", localPackage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("complex UCL engine", localPackage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnterpriseRelationshipEngineHandoff_IsGeneratedWithRequiredFields()
    {
        await using var harness = await NextActionHarness.CreateAsync();

        var result = await harness.Service.GenerateNextActionAsync(
            SaleRequest("email", "avery@example.test"),
            CancellationToken.None);

        Assert.NotNull(result);
        var handoff = JsonSerializer.Deserialize<UclEnterpriseRelationshipEngineHandoffV1>(
            result.EvidencePack.EnterpriseRelationshipEngineHandoffJson,
            JsonOptions);
        Assert.NotNull(handoff);
        var validation = UclEnterpriseRelationshipEngineHandoffV1Validator.Validate(handoff);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.Equal("ucl.enterprise-relationship-engine-handoff", handoff.ArtifactKind);
        Assert.Equal("ucl.enterprise-relationship-engine-handoff.v1", handoff.ArtifactVersion);
        Assert.Equal(result.EvidencePack.EvidencePackId, handoff.PackageId);
        Assert.Equal("KynticAI Scout", handoff.Producer);
        Assert.Equal("BasicRelationshipEngine", handoff.FallbackEngine);
        Assert.False(handoff.RequiresLiveEnterpriseService);
        Assert.False(handoff.EnterpriseOnlyInternalsIncluded);
        Assert.False(handoff.RelationshipWeighting.ScoutWeightsAreCanonical);
        Assert.Equal("Enterprise", handoff.RelationshipWeighting.CanonicalOwner);
        Assert.Equal(result.Relationships.Count, handoff.CandidateRelationships.Count);
        Assert.Equal(result.ExactLinkedRecords.Records.Count, handoff.EvidenceSummary.ExactRecordCount);
        Assert.Equal(result.Provenance.Count, handoff.EvidenceSummary.ProvenanceCitationCount);
        Assert.Contains(handoff.RequiredEnterpriseOutputs, x => x == "canonicalRelationshipWeights");
        Assert.Contains(handoff.RequiredEnterpriseOutputs, x => x == "canonicalTraversalSignals");

        var provenanceIds = handoff.Provenance.Select(x => x.CitationId).ToHashSet(StringComparer.Ordinal);
        Assert.All(handoff.CandidateRelationships, relationship =>
        {
            Assert.Equal("basic-public-fallback-only", relationship.FallbackWeightScope);
            Assert.NotEmpty(relationship.CitationIds);
            Assert.All(relationship.CitationIds, citationId => Assert.Contains(citationId, provenanceIds));
        });
    }

    [Fact]
    public async Task EnterpriseRelationshipEngineHandoff_ExcludesEnterpriseOnlyWeightInternals()
    {
        await using var harness = await NextActionHarness.CreateAsync();

        var result = await harness.Service.GenerateNextActionAsync(
            SaleRequest("email", "avery@example.test"),
            CancellationToken.None);

        Assert.NotNull(result);
        var handoffJson = result.EvidencePack.EnterpriseRelationshipEngineHandoffJson;
        var handoff = JsonNode.Parse(handoffJson)!.AsObject();

        Assert.False(handoff["enterpriseOnlyInternalsIncluded"]!.GetValue<bool>());
        Assert.False(handoff["requiresLiveEnterpriseService"]!.GetValue<bool>());
        AssertForbiddenPropertyAbsent(handoff, "canonicalWeight");
        AssertForbiddenPropertyAbsent(handoff, "enterpriseWeight");
        AssertForbiddenPropertyAbsent(handoff, "privateWeight");
        AssertForbiddenPropertyAbsent(handoff, "weightFormula");
        AssertForbiddenPropertyAbsent(handoff, "canonicalFormula");
        AssertForbiddenPropertyAbsent(handoff, "rustScoringConfig");
        AssertForbiddenPropertyAbsent(handoff, "lanceDbPath");
        AssertForbiddenPropertyAbsent(handoff, "embedding");
        AssertForbiddenPropertyAbsent(handoff, "vectorPipeline");
        Assert.DoesNotContain("UCL owns canonical relationship weighting", handoffJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Scout canonical weighting", handoffJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("complex UCL engine", handoffJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExactCustomerPayloadsRemainOnlyInLocalCustomerDataPlaneArtifacts()
    {
        await using var harness = await NextActionHarness.CreateAsync(openSupportTickets: 1, daysPastDue: 9);

        var result = await harness.Service.GenerateNextActionAsync(
            SaleRequest("email", "avery@example.test", purpose: "customer_outreach"),
            CancellationToken.None);

        Assert.NotNull(result);
        var localPackage = result.EvidencePack.LocalDerivedEvidencePackageJson;
        var cloudPayload = result.EvidencePack.CloudAggregateUsagePayloadJson;
        var persistedAuditPayloads = string.Join(
            "\n",
            await harness.ScoutDbContext.AuditEvents
                .Select(x => x.MetadataJson)
                .ToListAsync());

        foreach (var fragment in new[]
        {
            "Avery Stone",
            "avery@example.test",
            "Acme Corp",
            "acme.example",
            "Payment blocker blocks rollout",
            "\"exactLinkedRecords\"",
            "\"relationships\"",
            "\"weightedSignals\"",
            "\"recommendedAction\"",
            "\"citationId\""
        })
        {
            Assert.Contains(fragment, localPackage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(fragment, cloudPayload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(fragment, persistedAuditPayloads, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("\"payloadKind\":\"cloud-aggregate-usage\"", persistedAuditPayloads, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NextActionValidationFailures_DoNotEchoSensitivePayloads()
    {
        const string sensitivePayload = "rawCrmRecords Avery Stone avery@example.test +44 7700 900123 Payment blocker blocks rollout token=secret";
        var validator = new NextActionInputValidator();

        var result = validator.Validate(new NextActionInput(
            "demo",
            sensitivePayload,
            sensitivePayload,
            "send-generated-recommendation",
            sensitivePayload,
            "tenant_admin"));

        Assert.False(result.IsValid);
        var failureMessage = string.Join("\n", result.Errors.Select(x => x.ErrorMessage));

        foreach (var fragment in new[]
        {
            "rawCrmRecords",
            "Avery Stone",
            "avery@example.test",
            "+44 7700 900123",
            "Payment blocker blocks rollout",
            "token=secret"
        })
        {
            Assert.DoesNotContain(fragment, failureMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static NextActionInput SaleRequest(
        string subjectType,
        string subjectIdentifier,
        string actorRole = "tenant_admin",
        string purpose = "customer_outreach")
        => new("demo", subjectType, subjectIdentifier, "sale", purpose, actorRole);

    private static void AssertForbiddenPropertyAbsent(JsonNode? node, string propertyName)
    {
        if (node is JsonObject obj)
        {
            Assert.DoesNotContain(obj, property => property.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            foreach (var property in obj)
            {
                AssertForbiddenPropertyAbsent(property.Value, propertyName);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                AssertForbiddenPropertyAbsent(item, propertyName);
            }
        }
    }

    private sealed class NextActionHarness : IAsyncDisposable
    {
        private NextActionHarness(
            ScoutDbContext scoutDbContext,
            CustomerOpsDbContext customerOpsDbContext,
            INextActionIntelligenceService service)
        {
            ScoutDbContext = scoutDbContext;
            CustomerOpsDbContext = customerOpsDbContext;
            Service = service;
        }

        public ScoutDbContext ScoutDbContext { get; }

        public CustomerOpsDbContext CustomerOpsDbContext { get; }

        public INextActionIntelligenceService Service { get; }

        public static async Task<NextActionHarness> CreateAsync(
            int pricingVisits30d = 5,
            int emailReplies30d = 2,
            int activeDays30 = 22,
            int featureAdoptionScore = 84,
            int openOpportunityProbability = 74,
            int openSupportTickets = 0,
            int daysPastDue = 0,
            int paymentFailures30d = 0,
            OperatorRole role = OperatorRole.TenantAdmin)
        {
            var databaseName = $"next-action-{Guid.NewGuid():N}";
            var scoutOptions = new DbContextOptionsBuilder<ScoutDbContext>()
                .UseInMemoryDatabase($"{databaseName}-scout")
                .Options;
            var opsOptions = new DbContextOptionsBuilder<CustomerOpsDbContext>()
                .UseInMemoryDatabase($"{databaseName}-ops")
                .Options;
            var scoutDbContext = new ScoutDbContext(scoutOptions);
            var customerOpsDbContext = new CustomerOpsDbContext(opsOptions);
            var clock = new TestClock(new DateTime(2026, 06, 16, 12, 00, 00, DateTimeKind.Utc));
            var actor = new ActorContext(
                "test-actor",
                null,
                "demo",
                null,
                null,
                "actor@example.test",
                "Test Actor",
                role,
                IsAuthenticated: true,
                IsSystem: false);

            await SeedAsync(
                scoutDbContext,
                customerOpsDbContext,
                clock.UtcNow,
                pricingVisits30d,
                emailReplies30d,
                activeDays30,
                featureAdoptionScore,
                openOpportunityProbability,
                openSupportTickets,
                daysPastDue,
                paymentFailures30d);

            var service = new NextActionIntelligenceService(
                scoutDbContext,
                customerOpsDbContext,
                clock,
                new TestCurrentActorService(actor),
                new BasicRelationshipEngine(),
                new EnterpriseRelationshipEngineHandoff(),
                new NextActionInputValidator());

            return new NextActionHarness(scoutDbContext, customerOpsDbContext, service);
        }

        public async ValueTask DisposeAsync()
        {
            await ScoutDbContext.DisposeAsync();
            await CustomerOpsDbContext.DisposeAsync();
        }

        private static async Task SeedAsync(
            ScoutDbContext scoutDbContext,
            CustomerOpsDbContext customerOpsDbContext,
            DateTime utcNow,
            int pricingVisits30d,
            int emailReplies30d,
            int activeDays30,
            int featureAdoptionScore,
            int openOpportunityProbability,
            int openSupportTickets,
            int daysPastDue,
            int paymentFailures30d)
        {
            var tenant = Tenant.Create("demo", "Demo", utcNow);
            scoutDbContext.Tenants.Add(tenant);

            var opsTenant = CustomerOpsTenant.Create("demo", "Demo", utcNow);
            customerOpsDbContext.CustomerOpsTenants.Add(opsTenant);

            var subjectAccount = CustomerAccount.Create(opsTenant.Id, "acct-subject", "Acme Corp", "acme.example", "Logistics", "enterprise", "EMEA", "evaluation", "Dana", 600, 5_000_000m, utcNow);
            var subjectContact = CustomerContact.Create(opsTenant.Id, subjectAccount.Id, "contact-subject", "user-subject", "Avery Stone", "avery@example.test", "VP Revenue", "vp", "Revenue", "email", true, utcNow);
            customerOpsDbContext.CustomerAccounts.Add(subjectAccount);
            customerOpsDbContext.CustomerContacts.Add(subjectContact);

            customerOpsDbContext.SalesOpportunities.Add(SalesOpportunity.Create(
                opsTenant.Id,
                subjectAccount.Id,
                subjectContact.Id,
                "opp-subject-open",
                "Acme Corp Enterprise Expansion",
                "proposal",
                82_000m,
                openOpportunityProbability,
                utcNow.AddDays(18),
                "expansion",
                true,
                utcNow));
            customerOpsDbContext.SalesOpportunities.Add(SalesOpportunity.Create(
                opsTenant.Id,
                subjectAccount.Id,
                subjectContact.Id,
                "opp-subject-prior-won",
                "Acme Corp Prior Enterprise Outcome",
                "closed_won",
                68_000m,
                100,
                utcNow.AddDays(-42),
                "new-business",
                false,
                utcNow));
            customerOpsDbContext.SalesActivities.Add(SalesActivity.Create(
                opsTenant.Id,
                subjectAccount.Id,
                subjectContact.Id,
                "meeting",
                "outbound",
                "positive_reply",
                "Champion asked for implementation pricing and rollout plan.",
                utcNow.AddDays(-2),
                utcNow));

            for (var index = 0; index < emailReplies30d; index++)
            {
                customerOpsDbContext.EmailEngagementEvents.Add(EmailEngagementEvent.Create(
                    opsTenant.Id,
                    subjectContact.Id,
                    "Enterprise Expansion",
                    index == 0 ? "meeting_booked" : "reply",
                    "email",
                    "{}",
                    utcNow.AddDays(-index - 1),
                    utcNow));
            }

            for (var index = 0; index < pricingVisits30d; index++)
            {
                customerOpsDbContext.WebConversionEvents.Add(WebConversionEvent.Create(
                    opsTenant.Id,
                    subjectAccount.Id,
                    subjectContact.Id,
                    "pricing_viewed",
                    "pricing",
                    "enterprise-demand",
                    "email",
                    80m + index,
                    utcNow.AddDays(-index - 1),
                    utcNow));
            }

            for (var index = 0; index < openSupportTickets; index++)
            {
                customerOpsDbContext.SupportTickets.Add(SupportTicket.Create(
                    opsTenant.Id,
                    subjectAccount.Id,
                    subjectContact.Id,
                    $"ticket-subject-{index}",
                    index == 0 ? "critical" : "medium",
                    "open",
                    "billing",
                    "Payment blocker blocks rollout",
                    utcNow.AddDays(-3 - index),
                    null,
                    null,
                    utcNow));
            }
            customerOpsDbContext.SupportTickets.Add(SupportTicket.Create(
                opsTenant.Id,
                subjectAccount.Id,
                subjectContact.Id,
                "ticket-subject-closed",
                "low",
                "closed",
                "onboarding",
                "Historic onboarding question resolved",
                utcNow.AddDays(-20),
                utcNow.AddDays(-18),
                9,
                utcNow));

            customerOpsDbContext.ProductUsageSummaries.Add(ProductUsageSummary.Create(
                opsTenant.Id,
                subjectAccount.Id,
                subjectContact.Id,
                utcNow.Date,
                activeDays30,
                26,
                48,
                pricingVisits30d,
                76,
                88,
                100,
                featureAdoptionScore,
                utcNow));
            customerOpsDbContext.BillingMetrics.Add(BillingMetric.Create(
                opsTenant.Id,
                subjectAccount.Id,
                utcNow.Date,
                7_200m,
                86_400m,
                daysPastDue,
                paymentFailures30d,
                12,
                daysPastDue > 0 || paymentFailures30d > 0 ? "watch" : "healthy",
                utcNow));

            SeedOutcomeAccount(
                customerOpsDbContext,
                opsTenant.Id,
                "acct-won",
                "Larkspur Logistics",
                "larkspur.example",
                "contact-won",
                "Morgan Stone",
                "morgan@larkspur.example",
                "closed_won",
                100,
                24,
                88,
                6,
                2,
                utcNow);
            SeedOutcomeAccount(
                customerOpsDbContext,
                opsTenant.Id,
                "acct-lost",
                "Brindle Care",
                "brindle.example",
                "contact-lost",
                "Priya Stone",
                "priya@brindle.example",
                "closed_lost",
                0,
                8,
                31,
                1,
                0,
                utcNow);

            await scoutDbContext.SaveChangesAsync();
            await customerOpsDbContext.SaveChangesAsync();
        }

        private static void SeedOutcomeAccount(
            CustomerOpsDbContext dbContext,
            Guid tenantId,
            string externalAccountId,
            string accountName,
            string domain,
            string externalContactId,
            string contactName,
            string email,
            string stage,
            int probability,
            int activeDays30,
            int featureAdoptionScore,
            int pricingVisits30d,
            int emailReplies30d,
            DateTime utcNow)
        {
            var account = CustomerAccount.Create(tenantId, externalAccountId, accountName, domain, "Logistics", "enterprise", "EMEA", "customer", "Dana", 500, 4_000_000m, utcNow);
            var contact = CustomerContact.Create(tenantId, account.Id, externalContactId, externalContactId.Replace("contact-", "user-", StringComparison.Ordinal), contactName, email, "VP Revenue", "vp", "Revenue", "email", true, utcNow);
            dbContext.CustomerAccounts.Add(account);
            dbContext.CustomerContacts.Add(contact);
            dbContext.SalesOpportunities.Add(SalesOpportunity.Create(
                tenantId,
                account.Id,
                contact.Id,
                $"opp-{externalAccountId}",
                $"{accountName} Enterprise Outcome",
                stage,
                75_000m,
                probability,
                utcNow.AddDays(-28),
                "new-business",
                false,
                utcNow));
            dbContext.ProductUsageSummaries.Add(ProductUsageSummary.Create(
                tenantId,
                account.Id,
                contact.Id,
                utcNow.Date,
                activeDays30,
                22,
                44,
                pricingVisits30d,
                70,
                80,
                100,
                featureAdoptionScore,
                utcNow));
            for (var index = 0; index < emailReplies30d; index++)
            {
                dbContext.EmailEngagementEvents.Add(EmailEngagementEvent.Create(
                    tenantId,
                    contact.Id,
                    "Enterprise Expansion",
                    "reply",
                    "email",
                    "{}",
                    utcNow.AddDays(-index - 1),
                    utcNow));
            }
            for (var index = 0; index < pricingVisits30d; index++)
            {
                dbContext.WebConversionEvents.Add(WebConversionEvent.Create(
                    tenantId,
                    account.Id,
                    contact.Id,
                    "pricing_viewed",
                    "pricing",
                    "enterprise-demand",
                    "email",
                    70m + index,
                    utcNow.AddDays(-index - 1),
                    utcNow));
            }
            dbContext.BillingMetrics.Add(BillingMetric.Create(
                tenantId,
                account.Id,
                utcNow.Date,
                7_200m,
                86_400m,
                0,
                0,
                6,
                "healthy",
                utcNow));
        }
    }

    private sealed class TestClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class TestCurrentActorService(ActorContext actor) : ICurrentActorService
    {
        public ActorContext GetCurrentActor() => actor;
    }
}
