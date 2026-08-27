using System.Text.Json;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.AI;
using KynticAI.Scout.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KynticAI.Scout.UnitTests;

public sealed class SalesSupportAgentServiceTests
{
    [Fact]
    public void BuildContextPackage_FlagsWeakSignals_AndAssignsCitationIds()
    {
        var utcNow = new DateTime(2026, 05, 09, 12, 00, 00, DateTimeKind.Utc);
        var service = CreateService(new ContextPackageOptions
        {
            LowConfidenceThreshold = 0.75m,
            MinimumStrongFacts = 2
        });

        var tenant = Tenant.Create("demo", "Demo", utcNow);
        var userProfile = UserProfile.Create(tenant.Id, "123", "Avery Stone", "avery@example.com", "Northstar", "VP RevOps", "enterprise", utcNow, utcNow);
        var snapshot = ContextSnapshot.Create(
            tenant.Id,
            userProfile.Id,
            1,
            "Sales-ready profile.",
            0.79m,
            utcNow.AddMinutes(-30));

        snapshot.Facts.Add(ContextFact.Create(
            tenant.Id,
            snapshot.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "preferredChannel",
            "\"email\"",
            FactValueType.Enum,
            0.93m,
            utcNow.AddMinutes(-20),
            utcNow.AddMinutes(40),
            "Email is the preferred channel.",
            """[{"source":"crm"}]""",
            utcNow));
        snapshot.Facts.Add(ContextFact.Create(
            tenant.Id,
            snapshot.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "conversionProbability",
            "85",
            FactValueType.Number,
            0.64m,
            utcNow.AddMinutes(-19),
            utcNow.AddMinutes(41),
            "Conversion probability is informative but low confidence.",
            """[{"source":"warehouse"}]""",
            utcNow));
        snapshot.Facts.Add(ContextFact.Create(
            tenant.Id,
            snapshot.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "engagementLevel",
            "\"high\"",
            FactValueType.Enum,
            0.88m,
            utcNow.AddHours(-2),
            utcNow.AddMinutes(-10),
            "Engagement was high, but the signal has gone stale.",
            """[{"source":"product"}]""",
            utcNow));

        var result = service.BuildContextPackage(
            tenant,
            userProfile,
            snapshot,
            "Book a discovery call.",
            utcNow);

        Assert.Equal(3, result.Facts.Count);
        Assert.Equal(["FACT-01", "FACT-02", "FACT-03"], result.Facts.Select(fact => fact.CitationId).ToArray());
        Assert.True(result.HumanReviewRecommended);
        Assert.Contains(result.WeakSignalMessages, message => message.Contains("low confidence", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.WeakSignalMessages, message => message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.MissingInformation, item => item.Contains("planInterest", StringComparison.Ordinal));
        Assert.Contains("\"humanReviewRecommended\":true", result.ContextPackageJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"externalUserId\"", result.ContextPackageJson, StringComparison.Ordinal);
        Assert.Contains("\"privacy\"", result.ContextPackageJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsExternalConsumerRequired_WithoutModelExecution()
    {
        var utcNow = new DateTime(2026, 05, 09, 12, 00, 00, DateTimeKind.Utc);
        var promptTemplate = PromptTemplate.Create(
            Guid.NewGuid(),
            "Intelligent Sales Support v1",
            "Grounded sales orchestration prompt.",
            "Only use grounded facts.",
            "Cite facts and avoid inventing details.",
            "Create a grounded plan for {{user.fullName}} at {{user.companyName}}.",
            """{"type":"object"}""",
            """["Cite facts."]""",
            utcNow);

        var contextPackage = CreateStrongContextPackage(utcNow);
        var service = CreateService(new ContextPackageOptions
        {
            LowConfidenceThreshold = 0.75m,
            MinimumStrongFacts = 3
        });

        var promptEnvelope = service.BuildPromptEnvelope(promptTemplate, contextPackage, "gpt-5.5", "mock");
        var artifact = await service.GenerateAsync(
            promptTemplate,
            contextPackage,
            promptEnvelope,
            "mock",
            CancellationToken.None);

        // Scout core does not execute an AI model. The generation surface must
        // return an explicit external-consumer-required signal rather than
        // silently producing a scored recommendation.
        Assert.NotNull(artifact.FailureReason);
        Assert.Contains("Scout core does not execute AI models", artifact.FailureReason, StringComparison.Ordinal);
        Assert.Contains("Scout core does not execute AI models", artifact.ValidationErrorsJson, StringComparison.Ordinal);
        Assert.Equal(0, artifact.AttemptCount);
        Assert.Equal("{}", artifact.OutputJson);
        Assert.True(artifact.HumanReviewRecommended);
    }

    private static SalesSupportAgentService CreateService(ContextPackageOptions options)
    {
        return new SalesSupportAgentService(Options.Create(options));
    }

    private static SalesContextPackageResult CreateStrongContextPackage(DateTime utcNow)
    {
        var facts = new[]
        {
            new GroundedContextFactResult("FACT-01", Guid.NewGuid(), "conversionProbability", "Conversion Probability", "85", FactValueType.Number, 0.93m, utcNow.AddMinutes(-20), utcNow.AddMinutes(40), true, false, "Conversion probability is high.", """[{"source":"crm"}]"""),
            new GroundedContextFactResult("FACT-02", Guid.NewGuid(), "churnRisk", "Churn Risk", "12", FactValueType.Number, 0.88m, utcNow.AddMinutes(-19), utcNow.AddMinutes(41), true, false, "Churn risk is currently manageable.", """[{"source":"warehouse"}]"""),
            new GroundedContextFactResult("FACT-03", Guid.NewGuid(), "planInterest", "Plan Interest", "\"enterprise\"", FactValueType.Enum, 0.91m, utcNow.AddMinutes(-18), utcNow.AddMinutes(42), true, false, "Enterprise plan interest is explicit.", """[{"source":"crm"}]"""),
            new GroundedContextFactResult("FACT-04", Guid.NewGuid(), "engagementLevel", "Engagement Level", "\"high\"", FactValueType.Enum, 0.9m, utcNow.AddMinutes(-17), utcNow.AddMinutes(43), true, false, "Recent activity is high.", """[{"source":"product"}]"""),
            new GroundedContextFactResult("FACT-05", Guid.NewGuid(), "preferredChannel", "Preferred Channel", "\"email\"", FactValueType.Enum, 0.95m, utcNow.AddMinutes(-16), utcNow.AddMinutes(44), true, false, "Email is the preferred channel.", """[{"source":"crm"}]""")
        };

        var contextPackagePayload = new
        {
            packageVersion = "2026-05-09",
            salesObjective = "Book a discovery call for enterprise rollout.",
            privacy = new
            {
                classification = "internal-sales-context"
            },
            subject = new
            {
                fullName = "Avery Stone",
                companyName = "Northstar",
                jobTitle = "VP RevOps",
                segment = "enterprise"
            },
            snapshot = new
            {
                snapshotId = Guid.NewGuid(),
                summary = "Strong enterprise buying intent with active usage.",
                overallConfidence = 0.91m,
                generatedAtUtc = utcNow.AddMinutes(-10),
                isStale = false
            },
            humanReviewRecommended = false,
            missingInformation = Array.Empty<string>(),
            weakSignalMessages = Array.Empty<string>(),
            facts = facts.Select(fact => new
            {
                citationId = fact.CitationId,
                factId = fact.FactId,
                attributeKey = fact.AttributeKey,
                displayName = fact.DisplayName,
                value = JsonSerializer.Deserialize<JsonElement>(fact.ValueJson),
                valueJson = fact.ValueJson,
                valueType = fact.ValueType.ToString().ToUpperInvariant(),
                confidence = fact.Confidence,
                observedAtUtc = fact.ObservedAtUtc,
                freshUntilUtc = fact.FreshUntilUtc,
                isFresh = fact.IsFresh,
                isLowConfidence = fact.IsLowConfidence,
                explanation = fact.Explanation,
                provenance = JsonSerializer.Deserialize<JsonElement>(fact.ProvenanceJson)
            })
        };

        return new SalesContextPackageResult(
            Guid.NewGuid(),
            "demo",
            "123",
            "Avery Stone",
            "Northstar",
            "VP RevOps",
            "enterprise",
            "Book a discovery call for enterprise rollout.",
            "Strong enterprise buying intent with active usage.",
            0.91m,
            utcNow.AddMinutes(-10),
            false,
            false,
            [],
            [],
            facts,
            JsonSerializer.Serialize(contextPackagePayload));
    }
}
