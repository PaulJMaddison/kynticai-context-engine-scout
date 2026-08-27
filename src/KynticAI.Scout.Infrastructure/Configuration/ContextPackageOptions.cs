namespace KynticAI.Scout.Infrastructure.Configuration;

/// <summary>
/// Settings used while preparing governed context packages for external
/// consumers. These settings do not select or invoke an AI model.
/// </summary>
public sealed class ContextPackageOptions
{
    public const string SectionName = "ContextPackages";

    public decimal LowConfidenceThreshold { get; set; } = 0.75m;

    public int MinimumStrongFacts { get; set; } = 3;
}
