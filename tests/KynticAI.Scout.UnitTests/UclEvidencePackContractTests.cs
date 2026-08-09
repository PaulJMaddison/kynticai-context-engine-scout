using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Application.Validation;

namespace KynticAI.Scout.UnitTests;

public sealed class UclEvidencePackContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] RequiredScenarios =
    [
        "b2b-saas",
        "ecommerce",
        "support-churn",
        "recruitment",
        "finance-retention",
        "healthcare-operations"
    ];

    [Fact]
    public void FullLocalEvidencePackFixtures_HaveExactCitationsAndProvenance()
    {
        var fixtures = LoadFixtureArray("samples/evidence-pack/v1/local-full-evidence-packs.json");

        AssertScenarioCoverage(fixtures);
        foreach (var fixture in fixtures)
        {
            var payload = PayloadText(fixture);
            var package = JsonSerializer.Deserialize<UclEvidencePackV1>(payload, JsonOptions);

            Assert.NotNull(package);
            var validation = UclEvidencePackV1Validator.Validate(package);
            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
            Assert.NotEmpty(package.ExactLinkedRecords.Records);
            Assert.NotEmpty(package.Relationships);
            Assert.NotEmpty(package.WeightedSignals);
            Assert.NotEmpty(package.Provenance);
            Assert.False(package.RelationshipWeighting.ScoutWeightsAreCanonical);
            Assert.Equal("Enterprise", package.RelationshipWeighting.CanonicalOwner);

            var provenanceIds = package.Provenance.Select(x => x.CitationId).ToHashSet(StringComparer.Ordinal);
            foreach (var record in package.ExactLinkedRecords.Records)
            {
                Assert.Contains(record.CitationId, provenanceIds);
                Assert.Contains(package.Provenance, x =>
                    x.CitationId == record.CitationId
                    && x.SourceEntityType == record.RecordType
                    && x.SourceEntityId == record.RecordId);
            }
        }
    }

    [Fact]
    public void GovernedMaskedEvidencePackFixtures_ObeyGovernance()
    {
        var fixtures = LoadFixtureArray("samples/evidence-pack/v1/local-governed-masked-evidence-packs.json");

        AssertScenarioCoverage(fixtures);
        foreach (var fixture in fixtures)
        {
            var payload = PayloadText(fixture);
            var package = JsonSerializer.Deserialize<UclEvidencePackV1>(payload, JsonOptions);

            Assert.NotNull(package);
            var validation = UclEvidencePackV1Validator.Validate(package);
            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
            Assert.NotEmpty(package.Governance.MaskedFields);
            Assert.Contains(package.Governance.AppliedRules, x => x.StartsWith("mask-", StringComparison.Ordinal));
            Assert.All(package.ExactLinkedRecords.Records, record => Assert.True(record.IsMasked));
            Assert.All(package.Provenance, citation => Assert.True(citation.IsMasked));

            foreach (var forbidden in fixture!["forbiddenFragments"]!.AsArray().Select(x => x!.GetValue<string>()))
            {
                Assert.DoesNotContain(forbidden, payload, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void CloudAggregateUsageFixtures_ExcludeForbiddenRawAndDerivedFields()
    {
        var fixtures = LoadFixtureArray("samples/evidence-pack/v1/cloud-aggregate-usage-payloads.json");

        AssertScenarioCoverage(fixtures);
        foreach (var fixture in fixtures)
        {
            var payload = PayloadText(fixture);
            var validation = UclCloudAggregateUsageV1Validator.ValidateJson(payload);
            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));

            var aggregate = JsonSerializer.Deserialize<UclCloudAggregateUsageV1>(payload, JsonOptions);
            Assert.NotNull(aggregate);
            AssertCleanCloudBoundary(aggregate.DataBoundary);
            Assert.DoesNotContain("\"records\"", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"facts\"", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"snapshots\"", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"evidencePacks\"", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"prompts\"", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"generatedContent\"", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"recommendation\"", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"citations\"", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"relationshipTypes\"", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"weightedSignals\"", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"caveats\"", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"derivedRelationshipIntelligence\"", payload, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ScoutLocalContextBriefProofArtifact_IsSchemaValidAndLabelsFallbackProbabilities()
    {
        var payload = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "docs/proof-artifacts/scout-local-context-brief-2026-08-09.json"));
        var validation = UclScoutLocalContextBriefV1Validator.ValidateJson(payload);
        var root = JsonNode.Parse(payload)!.AsObject();

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.Equal("scout-local-context-brief", root["brief_kind"]!.GetValue<string>());
        Assert.Equal("context-engine.scout-local-context-brief.v1", root["brief_version"]!.GetValue<string>());
        Assert.True(root["source_scope"]!["synthetic"]!.GetValue<bool>());
        Assert.True(root["source_scope"]!["authorised_for_local_proof"]!.GetValue<bool>());

        var actionOptions = root["action_options"]!.AsArray();
        var actions = actionOptions
            .Select(action => action!["action"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("email_customer_with_x_offer", actions);
        Assert.Contains("offer_prize_draw_registration", actions);
        Assert.Contains("do_not_reply", actions);
        Assert.All(actionOptions, action =>
        {
            Assert.Equal("local_basic_proof_not_fortress_canonical", action!["probability_label"]!.GetValue<string>());
            Assert.NotEmpty(action["evidence_ids"]!.AsArray());
            Assert.NotEmpty(action["caveats"]!.AsArray());
        });

        Assert.True(root["handoff"]!["fortress_required_for_canonical_scoring"]!.GetValue<bool>());
    }

    [Fact]
    public void ScoutMiniFlywheelReport_RecordsImprovementAndCloudBoundary()
    {
        var report = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "docs/proof-artifacts/scout-mini-flywheel-report-2026-08-09.md"));

        Assert.Contains("Context Engine - Scout local behaviour", report, StringComparison.Ordinal);
        Assert.Contains("not Fortress canonical production scoring", report, StringComparison.Ordinal);
        Assert.Contains("top-action hit rate rises from `0.50` to `0.67`", report, StringComparison.Ordinal);
        Assert.Contains("Cloud/control-plane payloads must remain aggregate/control-plane only", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Cloud stores customer intelligence", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnterpriseRelationshipEngineHandoffSample_IsValidAndPublicSafe()
    {
        var payload = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "samples/relationship-intelligence/enterprise-canonical-weighting-handoff.sample.json"));
        var validation = UclEnterpriseRelationshipEngineHandoffV1Validator.ValidateJson(payload);
        var artifact = JsonSerializer.Deserialize<UclEnterpriseRelationshipEngineHandoffV1>(payload, JsonOptions);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.NotNull(artifact);
        Assert.Equal("ucl.enterprise-relationship-engine-handoff", artifact.ArtifactKind);
        Assert.Equal("BasicRelationshipEngine", artifact.FallbackEngine);
        Assert.False(artifact.RequiresLiveEnterpriseService);
        Assert.False(artifact.EnterpriseOnlyInternalsIncluded);
        Assert.False(artifact.RelationshipWeighting.ScoutWeightsAreCanonical);
        Assert.Equal("Enterprise", artifact.RelationshipWeighting.CanonicalOwner);
        Assert.NotEmpty(artifact.CandidateRelationships);
        Assert.Contains(artifact.RequiredEnterpriseOutputs, output => output == "canonicalRelationshipWeights");
        Assert.Contains(artifact.RequiredEnterpriseOutputs, output => output == "canonicalTraversalSignals");
        AssertForbiddenPropertyAbsent(JsonNode.Parse(payload), "canonicalWeight");
        AssertForbiddenPropertyAbsent(JsonNode.Parse(payload), "enterpriseWeight");
        AssertForbiddenPropertyAbsent(JsonNode.Parse(payload), "privateWeight");
        AssertForbiddenPropertyAbsent(JsonNode.Parse(payload), "weightFormula");
        AssertForbiddenPropertyAbsent(JsonNode.Parse(payload), "rustScoringConfig");
        AssertForbiddenPropertyAbsent(JsonNode.Parse(payload), "vectorPipeline");
    }

    [Fact]
    public void UnsafeCloudPayloadExamples_AreRejectedByLocalValidator()
    {
        var fixtures = LoadFixtureArray("samples/evidence-pack/v1/unsafe-cloud-payload-examples.json");

        AssertScenarioCoverage(fixtures);
        foreach (var fixture in fixtures)
        {
            var payload = PayloadText(fixture);
            var validation = UclCloudAggregateUsageV1Validator.ValidateJson(payload);

            Assert.False(validation.IsValid);
            Assert.NotEmpty(validation.Errors);
        }
    }

    private static void AssertCleanCloudBoundary(UclCloudDataBoundaryV1 boundary)
    {
        Assert.True(boundary.RawDataRetainedInCustomerDataPlane);
        Assert.False(boundary.ContainsRawCustomerData);
        Assert.False(boundary.ContainsRecords);
        Assert.False(boundary.ContainsFacts);
        Assert.False(boundary.ContainsContextFacts);
        Assert.False(boundary.ContainsSnapshots);
        Assert.False(boundary.ContainsContextSnapshots);
        Assert.False(boundary.ContainsEvidencePacks);
        Assert.False(boundary.ContainsPrompts);
        Assert.False(boundary.ContainsGeneratedContent);
        Assert.False(boundary.ContainsRecommendations);
        Assert.False(boundary.ContainsCitations);
        Assert.False(boundary.ContainsCitationIds);
        Assert.False(boundary.ContainsRelationshipTypes);
        Assert.False(boundary.ContainsWeightedSignals);
        Assert.False(boundary.ContainsCaveats);
        Assert.False(boundary.ContainsPerEntityRelationshipMetadata);
        Assert.False(boundary.ContainsDerivedRelationshipIntelligence);
        Assert.False(boundary.ContainsPerCustomerDerivedIntelligence);
    }

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

    private static JsonArray LoadFixtureArray(string relativePath)
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath)))!.AsObject();
        return root["fixtures"]!.AsArray();
    }

    private static string PayloadText(JsonNode? fixture)
        => fixture!["payload"]!.ToJsonString();

    private static void AssertScenarioCoverage(JsonArray fixtures)
    {
        var scenarios = fixtures
            .Select(x => x!["scenario"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(RequiredScenarios.Length, scenarios.Count);
        foreach (var scenario in RequiredScenarios)
        {
            Assert.Contains(scenario, scenarios);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "KynticAI.Scout.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
