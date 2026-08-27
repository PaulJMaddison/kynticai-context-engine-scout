namespace KynticAI.Scout.Reference.Sales;

/// <summary>
/// Prompt-envelope contracts for the fictional sales reference consumer.
/// They intentionally live outside Scout core.
/// </summary>
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
