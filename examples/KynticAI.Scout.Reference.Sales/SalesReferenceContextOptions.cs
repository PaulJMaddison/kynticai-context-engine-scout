namespace KynticAI.Scout.Reference.Sales;

/// <summary>
/// Quality thresholds used only by the fictional sales reference example.
/// They are example-consumer settings, not Scout platform scoring semantics.
/// </summary>
public sealed class SalesReferenceContextOptions
{
    public decimal LowConfidenceThreshold { get; set; } = 0.75m;

    public int MinimumStrongFacts { get; set; } = 3;
}
