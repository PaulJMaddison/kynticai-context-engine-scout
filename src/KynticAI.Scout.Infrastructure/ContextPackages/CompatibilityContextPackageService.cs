using System.Globalization;
using System.Text.Json;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KynticAI.Scout.Infrastructure.ContextPackages;

/// <summary>
/// Compatibility implementation for the legacy sales-context contract.
///
/// Scout core may package already-derived, source-traced context for an external
/// consumer, but it does not define required sales attributes, sales weights,
/// recommendation rules, prompt orchestration, or model execution.
/// </summary>
public sealed class CompatibilityContextPackageService(
    IOptions<ContextPackageOptions> options)
    : ISalesContextPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public SalesContextPackageResult BuildContextPackage(
        Tenant tenant,
        UserProfile userProfile,
        ContextSnapshot contextSnapshot,
        string salesObjective,
        DateTime utcNow)
    {
        var contextOptions = options.Value;
        var strongFacts = 0;
        var factResults = contextSnapshot.Facts
            .OrderBy(fact => fact.AttributeKey, StringComparer.Ordinal)
            .Select((fact, index) =>
            {
                var isFresh = !fact.FreshUntilUtc.HasValue || fact.FreshUntilUtc.Value >= utcNow;
                var isLowConfidence = fact.Confidence < contextOptions.LowConfidenceThreshold;
                if (isFresh && !isLowConfidence)
                {
                    strongFacts++;
                }

                return new GroundedContextFactResult(
                    CitationId: $"FACT-{index + 1:00}",
                    FactId: fact.Id,
                    AttributeKey: fact.AttributeKey,
                    DisplayName: fact.SemanticAttributeDefinition?.DisplayName ?? fact.AttributeKey,
                    ValueJson: fact.ValueJson,
                    ValueType: fact.ValueType,
                    Confidence: fact.Confidence,
                    ObservedAtUtc: fact.ObservedAtUtc,
                    FreshUntilUtc: fact.FreshUntilUtc,
                    IsFresh: isFresh,
                    IsLowConfidence: isLowConfidence,
                    Explanation: fact.Explanation,
                    ProvenanceJson: fact.ProvenanceJson);
            })
            .ToList();

        var weakSignalMessages = new List<string>();
        if (contextSnapshot.IsStale)
        {
            weakSignalMessages.Add("The latest context snapshot is marked stale and should be treated as provisional.");
        }

        foreach (var fact in factResults)
        {
            if (!fact.IsFresh)
            {
                weakSignalMessages.Add($"{fact.DisplayName} is stale and should be revalidated before acting.");
            }

            if (fact.IsLowConfidence)
            {
                weakSignalMessages.Add($"{fact.DisplayName} is low confidence at {fact.Confidence.ToString("P0", CultureInfo.InvariantCulture)}.");
            }
        }

        if (strongFacts < contextOptions.MinimumStrongFacts)
        {
            weakSignalMessages.Add(
                $"Only {strongFacts} fresh high-confidence facts are available, which is below the configured context-package threshold of {contextOptions.MinimumStrongFacts}.");
        }

        var contextPackagePayload = new
        {
            packageVersion = "2026-08-27",
            contractStatus = "legacy-sales-context-compatibility",
            salesObjective = salesObjective.Trim(),
            privacy = new
            {
                classification = "internal-context",
                provenanceRequired = true
            },
            subject = new
            {
                fullName = userProfile.FullName,
                companyName = userProfile.CompanyName,
                jobTitle = userProfile.JobTitle,
                segment = userProfile.Segment
            },
            snapshot = new
            {
                snapshotId = contextSnapshot.Id,
                summary = contextSnapshot.Summary,
                overallConfidence = contextSnapshot.OverallConfidence,
                generatedAtUtc = contextSnapshot.GeneratedAtUtc,
                isStale = contextSnapshot.IsStale
            },
            humanReviewRecommended = weakSignalMessages.Count > 0,
            missingInformation = Array.Empty<string>(),
            weakSignalMessages,
            facts = factResults.Select(fact => new
            {
                citationId = fact.CitationId,
                factId = fact.FactId,
                attributeKey = fact.AttributeKey,
                displayName = fact.DisplayName,
                value = DeserializeJsonValue(fact.ValueJson),
                valueJson = fact.ValueJson,
                valueType = fact.ValueType.ToString().ToUpperInvariant(),
                confidence = fact.Confidence,
                observedAtUtc = fact.ObservedAtUtc,
                freshUntilUtc = fact.FreshUntilUtc,
                isFresh = fact.IsFresh,
                isLowConfidence = fact.IsLowConfidence,
                explanation = fact.Explanation,
                provenance = DeserializeJsonValue(fact.ProvenanceJson)
            })
        };

        return new SalesContextPackageResult(
            SnapshotId: contextSnapshot.Id,
            TenantSlug: tenant.Slug,
            ExternalUserId: userProfile.ExternalUserId,
            FullName: userProfile.FullName,
            CompanyName: userProfile.CompanyName,
            JobTitle: userProfile.JobTitle,
            Segment: userProfile.Segment,
            SalesObjective: salesObjective.Trim(),
            Summary: contextSnapshot.Summary,
            OverallConfidence: contextSnapshot.OverallConfidence,
            GeneratedAtUtc: contextSnapshot.GeneratedAtUtc,
            IsStale: contextSnapshot.IsStale || factResults.Any(fact => !fact.IsFresh),
            HumanReviewRecommended: weakSignalMessages.Count > 0,
            MissingInformation: Array.Empty<string>(),
            WeakSignalMessages: weakSignalMessages,
            Facts: factResults,
            ContextPackageJson: JsonSerializer.Serialize(contextPackagePayload, JsonOptions));
    }

    private static object? DeserializeJsonValue(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
