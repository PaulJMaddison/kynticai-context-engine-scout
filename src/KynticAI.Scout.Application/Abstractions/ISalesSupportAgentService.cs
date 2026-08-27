using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Entities;

namespace KynticAI.Scout.Application.Abstractions;

public interface ISalesSupportAgentService
{
    SalesContextPackageResult BuildContextPackage(
        Tenant tenant,
        UserProfile userProfile,
        ContextSnapshot contextSnapshot,
        string salesObjective,
        DateTime utcNow);

    SalesSupportPromptEnvelope BuildPromptEnvelope(
        PromptTemplate promptTemplate,
        SalesContextPackageResult contextPackage,
        string modelName,
        string providerName);

    Task<SalesSupportGenerationArtifact> GenerateAsync(
        PromptTemplate promptTemplate,
        SalesContextPackageResult contextPackage,
        SalesSupportPromptEnvelope promptEnvelope,
        string? providerName,
        CancellationToken cancellationToken);
}

public sealed record LlmPromptMessage(
    string Role,
    string Content);

public sealed record SalesSupportPromptEnvelope(
    IReadOnlyList<LlmPromptMessage> Messages,
    string InputJson);

public sealed record SalesSupportGenerationArtifact(
    string ProviderName,
    string ModelName,
    string SalesObjective,
    decimal Confidence,
    int AttemptCount,
    bool HumanReviewRecommended,
    string ContextPackageJson,
    string OutputJson,
    string ProvenanceJson,
    string ValidationErrorsJson,
    string? FailureReason);
