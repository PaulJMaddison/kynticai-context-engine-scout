using System.Globalization;
using System.Text.Json;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Constants;
using KynticAI.Scout.Domain.Entities;

namespace KynticAI.Scout.Reference.Sales;

public sealed class SalesSupportAgentService(
    SalesReferenceContextOptions options)
    : ISalesSupportAgentService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyList<string> RequiredSalesAttributeKeys =
    [
        SemanticAttributeKeys.ConversionProbability,
        SemanticAttributeKeys.PreferredChannel,
        SemanticAttributeKeys.PlanInterest,
        SemanticAttributeKeys.ChurnRisk,
        SemanticAttributeKeys.EngagementLevel
    ];

    public SalesContextPackageResult BuildContextPackage(
        Tenant tenant,
        UserProfile userProfile,
        ContextSnapshot contextSnapshot,
        string salesObjective,
        DateTime utcNow)
    {
        var contextOptions = options;
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

        var missingInformation = RequiredSalesAttributeKeys
            .Where(requiredKey => factResults.All(fact => !string.Equals(fact.AttributeKey, requiredKey, StringComparison.OrdinalIgnoreCase)))
            .Select(requiredKey => $"No grounded fact is currently available for '{requiredKey}'.")
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
            weakSignalMessages.Add($"Only {strongFacts} fresh high-confidence facts are available, which is below the minimum grounded threshold of {contextOptions.MinimumStrongFacts}.");
        }

        var humanReviewRecommended = weakSignalMessages.Count > 0 || missingInformation.Count > 0;

        var contextPackagePayload = new
        {
            packageVersion = "2026-05-09",
            salesObjective = salesObjective.Trim(),
            privacy = new
            {
                classification = "internal-sales-context",
                piiPolicy = new
                {
                    excludedFields = new[] { "email", "phone", "streetAddress", "notes" },
                    includedSubjectFields = new[] { "fullName", "companyName", "jobTitle", "segment" },
                    maskedFields = Array.Empty<string>(),
                    provenanceRequired = true
                }
            },
            subject = new
            {
                fullName = userProfile.FullName,
                companyName = userProfile.CompanyName,
                jobTitle = userProfile.JobTitle,
                segment = userProfile.Segment
            },
            subjectProvenance = new
            {
                sourceEntity = nameof(UserProfile),
                tenantSlug = tenant.Slug,
                fields = new[]
                {
                    new { field = "fullName", value = userProfile.FullName, source = "user_profiles.full_name", piiClassification = "direct_identifier" },
                    new { field = "companyName", value = userProfile.CompanyName, source = "user_profiles.company_name", piiClassification = "business_contact" },
                    new { field = "jobTitle", value = userProfile.JobTitle, source = "user_profiles.job_title", piiClassification = "business_contact" },
                    new { field = "segment", value = userProfile.Segment, source = "user_profiles.segment", piiClassification = "account_metadata" }
                }
            },
            snapshot = new
            {
                snapshotId = contextSnapshot.Id,
                summary = contextSnapshot.Summary,
                overallConfidence = contextSnapshot.OverallConfidence,
                generatedAtUtc = contextSnapshot.GeneratedAtUtc,
                isStale = contextSnapshot.IsStale
            },
            humanReviewRecommended,
            missingInformation,
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
            HumanReviewRecommended: humanReviewRecommended,
            MissingInformation: missingInformation,
            WeakSignalMessages: weakSignalMessages,
            Facts: factResults,
            ContextPackageJson: JsonSerializer.Serialize(contextPackagePayload, JsonOptions));
    }

    public SalesSupportPromptEnvelope BuildPromptEnvelope(
        PromptTemplate promptTemplate,
        SalesContextPackageResult contextPackage,
        string modelName,
        string providerName)
    {
        var guardrails = ParseStringArray(promptTemplate.GuardrailsJson);
        var tokens = BuildPromptTokens(contextPackage);

        var messages = new List<LlmPromptMessage>
        {
            new("system", RenderTemplate(promptTemplate.SystemPrompt, tokens)),
            new("developer", RenderDeveloperPrompt(promptTemplate, contextPackage, guardrails, tokens)),
            new("user", RenderUserPrompt(promptTemplate, contextPackage, tokens))
        };

        var inputPayload = JsonSerializer.Serialize(new
        {
            providerName,
            modelName,
            salesObjective = contextPackage.SalesObjective,
            promptTemplate = new
            {
                promptTemplate.Id,
                promptTemplate.Name,
                promptTemplate.Version,
                promptTemplate.SystemPrompt,
                promptTemplate.DeveloperPrompt,
                promptTemplate.UserPromptTemplate,
                promptTemplate.OutputSchemaJson,
                guardrails
            },
            promptMessages = messages,
            contextPackage = DeserializeJsonValue(contextPackage.ContextPackageJson)
        }, JsonOptions);

        return new SalesSupportPromptEnvelope(messages, inputPayload);
    }

    public Task<SalesSupportGenerationArtifact> GenerateAsync(
        PromptTemplate promptTemplate,
        SalesContextPackageResult contextPackage,
        SalesSupportPromptEnvelope promptEnvelope,
        string? providerName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var modelName = ExtractModelNameFromEnvelope(promptEnvelope.InputJson) ?? "external";
        var selectedProvider = string.IsNullOrWhiteSpace(providerName)
            ? "external"
            : providerName.Trim();
        const string reason =
            "The Scout sales reference project does not execute AI models. Send the governed context package and prompt envelope " +
            "to a customer-owned or reference consumer and perform inference there.";

        return Task.FromResult(new SalesSupportGenerationArtifact(
            ProviderName: selectedProvider,
            ModelName: modelName,
            SalesObjective: contextPackage.SalesObjective,
            Confidence: 0m,
            AttemptCount: 0,
            HumanReviewRecommended: true,
            ContextPackageJson: contextPackage.ContextPackageJson,
            OutputJson: "{}",
            ProvenanceJson: "[]",
            ValidationErrorsJson: JsonSerializer.Serialize(new[] { reason }, JsonOptions),
            FailureReason: reason));
    }

    private static string RenderDeveloperPrompt(
        PromptTemplate promptTemplate,
        SalesContextPackageResult contextPackage,
        IReadOnlyList<string> guardrails,
        IReadOnlyDictionary<string, string> tokens)
    {
        var basePrompt = RenderTemplate(promptTemplate.DeveloperPrompt, tokens);
        var guardrailBlock = string.Join(Environment.NewLine, guardrails.Select(rule => $"- {rule}"));
        return
            $"{basePrompt}{Environment.NewLine}{Environment.NewLine}" +
            $"Use the following guardrails without exception:{Environment.NewLine}{guardrailBlock}{Environment.NewLine}{Environment.NewLine}" +
            $"If any fact is stale or low confidence, say so explicitly and recommend human review.{Environment.NewLine}" +
            $"Return only JSON that conforms to this schema:{Environment.NewLine}{promptTemplate.OutputSchemaJson}{Environment.NewLine}{Environment.NewLine}" +
            $"Current weak signals:{Environment.NewLine}{string.Join(Environment.NewLine, contextPackage.WeakSignalMessages.DefaultIfEmpty("None."))}";
    }

    private static string RenderUserPrompt(
        PromptTemplate promptTemplate,
        SalesContextPackageResult contextPackage,
        IReadOnlyDictionary<string, string> tokens)
    {
        return
            $"{RenderTemplate(promptTemplate.UserPromptTemplate, tokens)}{Environment.NewLine}{Environment.NewLine}" +
            $"Context package JSON:{Environment.NewLine}{contextPackage.ContextPackageJson}";
    }

    private static IReadOnlyDictionary<string, string> BuildPromptTokens(SalesContextPackageResult contextPackage)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["salesObjective"] = contextPackage.SalesObjective,
            ["summary"] = contextPackage.Summary,
            ["snapshot.summary"] = contextPackage.Summary,
            ["subject.fullName"] = contextPackage.FullName,
            ["subject.companyName"] = contextPackage.CompanyName,
            ["subject.jobTitle"] = contextPackage.JobTitle,
            ["subject.segment"] = contextPackage.Segment,
            ["user.fullName"] = contextPackage.FullName,
            ["user.companyName"] = contextPackage.CompanyName,
            ["user.jobTitle"] = contextPackage.JobTitle
        };
    }

    private static string RenderTemplate(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var rendered = template;
        foreach (var token in tokens)
        {
            rendered = rendered.Replace($"{{{{{token.Key}}}}}", token.Value, StringComparison.OrdinalIgnoreCase);
        }

        return rendered;
    }

    private static IReadOnlyList<string> ParseStringArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
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

    private static string? ExtractModelNameFromEnvelope(string inputJson)
    {
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            if (document.RootElement.TryGetProperty("modelName", out var modelProperty))
            {
                return modelProperty.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static List<string> ValidateSalesSupportResponse(
        string outputJson,
        SalesContextPackageResult contextPackage,
        out SalesSupportResponse? parsedResponse)
    {
        parsedResponse = null;
        try
        {
            parsedResponse = JsonSerializer.Deserialize<SalesSupportResponse>(outputJson, JsonOptions);
        }
        catch (JsonException exception)
        {
            return [$"Model output is not valid JSON: {exception.Message}"];
        }

        if (parsedResponse is null)
        {
            return ["Model output could not be deserialized into the expected response contract."];
        }

        var errors = new List<string>();
        var validCitationIds = contextPackage.Facts.Select(fact => fact.CitationId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(parsedResponse.SalesObjective))
        {
            errors.Add("salesObjective is required.");
        }

        if (parsedResponse.OutreachStrategy is null)
        {
            errors.Add("outreachStrategy is required.");
        }
        else
        {
            ValidateInsightCollection(parsedResponse.OutreachStrategy.KeyTalkingPoints, "outreachStrategy.keyTalkingPoints", validCitationIds, errors);
            ValidateInsightCollection(parsedResponse.OutreachStrategy.Risks, "outreachStrategy.risks", validCitationIds, errors);
            ValidateConfidence(parsedResponse.OutreachStrategy.Confidence, "outreachStrategy.confidence", errors);
            if (string.IsNullOrWhiteSpace(parsedResponse.OutreachStrategy.Summary))
            {
                errors.Add("outreachStrategy.summary is required.");
            }
        }

        if (parsedResponse.PersonalizedEmailDraft is null)
        {
            errors.Add("personalizedEmailDraft is required.");
        }
        else
        {
            ValidateInsightCollection(parsedResponse.PersonalizedEmailDraft.SupportingClaims, "personalizedEmailDraft.supportingClaims", validCitationIds, errors);
            ValidateConfidence(parsedResponse.PersonalizedEmailDraft.Confidence, "personalizedEmailDraft.confidence", errors);
            if (string.IsNullOrWhiteSpace(parsedResponse.PersonalizedEmailDraft.SubjectLine))
            {
                errors.Add("personalizedEmailDraft.subjectLine is required.");
            }

            if (string.IsNullOrWhiteSpace(parsedResponse.PersonalizedEmailDraft.Body))
            {
                errors.Add("personalizedEmailDraft.body is required.");
            }
        }

        if (parsedResponse.FollowUpRecommendations is null)
        {
            errors.Add("followUpRecommendations is required.");
        }
        else
        {
            ValidateRecommendationCollection(parsedResponse.FollowUpRecommendations.Recommendations, validCitationIds, errors);
            ValidateConfidence(parsedResponse.FollowUpRecommendations.Confidence, "followUpRecommendations.confidence", errors);

            if (contextPackage.WeakSignalMessages.Count > 0
                && parsedResponse.FollowUpRecommendations.LowConfidenceSignals.Count == 0)
            {
                errors.Add("followUpRecommendations.lowConfidenceSignals must acknowledge weak signals when the context package contains them.");
            }
        }

        ValidateConfidence(parsedResponse.OverallConfidence, "overallConfidence", errors);

        if (contextPackage.HumanReviewRecommended && !parsedResponse.HumanReviewRecommended)
        {
            errors.Add("humanReviewRecommended must be true when the context package is weak or incomplete.");
        }

        foreach (var missingInformation in contextPackage.MissingInformation)
        {
            if (!parsedResponse.MissingInformation.Contains(missingInformation, StringComparer.Ordinal))
            {
                errors.Add($"missingInformation must include '{missingInformation}'.");
            }
        }

        return errors;
    }

    private static void ValidateInsightCollection(
        IReadOnlyList<CitedInsight> insights,
        string fieldPath,
        IReadOnlySet<string> validCitationIds,
        ICollection<string> errors)
    {
        if (insights.Count == 0)
        {
            errors.Add($"{fieldPath} must contain at least one cited item.");
            return;
        }

        for (var index = 0; index < insights.Count; index++)
        {
            var item = insights[index];
            if (string.IsNullOrWhiteSpace(item.Text))
            {
                errors.Add($"{fieldPath}[{index}].text is required.");
            }

            ValidateCitations(item.Citations, $"{fieldPath}[{index}].citations", validCitationIds, errors);
            ValidateConfidence(item.Confidence, $"{fieldPath}[{index}].confidence", errors);
        }
    }

    private static void ValidateRecommendationCollection(
        IReadOnlyList<FollowUpRecommendation> recommendations,
        IReadOnlySet<string> validCitationIds,
        ICollection<string> errors)
    {
        if (recommendations.Count == 0)
        {
            errors.Add("followUpRecommendations.recommendations must contain at least one item.");
            return;
        }

        for (var index = 0; index < recommendations.Count; index++)
        {
            var item = recommendations[index];
            if (string.IsNullOrWhiteSpace(item.Action))
            {
                errors.Add($"followUpRecommendations.recommendations[{index}].action is required.");
            }

            if (string.IsNullOrWhiteSpace(item.Rationale))
            {
                errors.Add($"followUpRecommendations.recommendations[{index}].rationale is required.");
            }

            ValidateCitations(item.Citations, $"followUpRecommendations.recommendations[{index}].citations", validCitationIds, errors);
            ValidateConfidence(item.Confidence, $"followUpRecommendations.recommendations[{index}].confidence", errors);
        }
    }

    private static void ValidateCitations(
        IReadOnlyList<string> citations,
        string fieldPath,
        IReadOnlySet<string> validCitationIds,
        ICollection<string> errors)
    {
        if (citations.Count == 0)
        {
            errors.Add($"{fieldPath} must contain at least one citation.");
            return;
        }

        foreach (var citation in citations)
        {
            if (!validCitationIds.Contains(citation))
            {
                errors.Add($"{fieldPath} contains unknown citation '{citation}'.");
            }
        }
    }

    private static void ValidateConfidence(decimal confidence, string fieldPath, ICollection<string> errors)
    {
        if (confidence < 0m || confidence > 1m)
        {
            errors.Add($"{fieldPath} must be between 0 and 1.");
        }
    }

    private static string BuildProvenanceJson(SalesSupportResponse response, SalesContextPackageResult contextPackage)
    {
        var usedCitationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectCitations(response.OutreachStrategy.KeyTalkingPoints, usedCitationIds);
        CollectCitations(response.OutreachStrategy.Risks, usedCitationIds);
        CollectCitations(response.PersonalizedEmailDraft.SupportingClaims, usedCitationIds);
        foreach (var recommendation in response.FollowUpRecommendations.Recommendations)
        {
            foreach (var citation in recommendation.Citations)
            {
                usedCitationIds.Add(citation);
            }
        }

        var provenance = contextPackage.Facts
            .Where(fact => usedCitationIds.Contains(fact.CitationId))
            .Select(fact => new
            {
                fact.CitationId,
                fact.FactId,
                fact.AttributeKey,
                fact.DisplayName,
                fact.ValueJson,
                fact.Confidence,
                fact.ObservedAtUtc,
                fact.FreshUntilUtc,
                fact.IsFresh,
                fact.IsLowConfidence,
                fact.Explanation,
                provenance = DeserializeJsonValue(fact.ProvenanceJson)
            });

        return JsonSerializer.Serialize(provenance, JsonOptions);
    }

    private static void CollectCitations(IEnumerable<CitedInsight> insights, ISet<string> destination)
    {
        foreach (var insight in insights)
        {
            foreach (var citation in insight.Citations)
            {
                destination.Add(citation);
            }
        }
    }

    private sealed record SalesSupportResponse(
        string SalesObjective,
        OutreachStrategy OutreachStrategy,
        PersonalizedEmailDraft PersonalizedEmailDraft,
        FollowUpRecommendationSet FollowUpRecommendations,
        IReadOnlyList<string> MissingInformation,
        bool HumanReviewRecommended,
        string HumanReviewReason,
        decimal OverallConfidence);

    private sealed record OutreachStrategy(
        string Summary,
        string RecommendedChannel,
        string TimingRecommendation,
        IReadOnlyList<CitedInsight> KeyTalkingPoints,
        IReadOnlyList<CitedInsight> Risks,
        decimal Confidence,
        bool HumanReviewRecommended,
        string HumanReviewReason);

    private sealed record PersonalizedEmailDraft(
        string SubjectLine,
        string PreviewText,
        string Body,
        string CallToAction,
        IReadOnlyList<CitedInsight> SupportingClaims,
        decimal Confidence,
        bool HumanReviewRecommended,
        string HumanReviewReason);

    private sealed record FollowUpRecommendationSet(
        IReadOnlyList<FollowUpRecommendation> Recommendations,
        IReadOnlyList<string> LowConfidenceSignals,
        IReadOnlyList<string> MissingInformation,
        decimal Confidence,
        bool HumanReviewRecommended,
        string HumanReviewReason);

    private sealed record FollowUpRecommendation(
        string Action,
        string Timing,
        string Rationale,
        IReadOnlyList<string> Citations,
        decimal Confidence);

    private sealed record CitedInsight(
        string Text,
        IReadOnlyList<string> Citations,
        decimal Confidence);
}
