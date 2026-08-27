using KynticAI.Scout.Application.Contracts;

namespace KynticAI.Scout.Application.Services;

/// <summary>
/// Scout core deliberately does not own sales/RevOps decision heuristics.
/// The legacy next-action contract remains for compatibility while the
/// reference implementation is consumed outside the core runtime.
/// </summary>
public sealed class DisabledNextActionIntelligenceService : INextActionIntelligenceService
{
    public Task<NextActionResult?> GenerateNextActionAsync(
        NextActionInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException(
            "Sales next-action scoring is a reference use case, not Scout core behaviour. " +
            "Read governed Scout context through the public API/SDK and run decision logic in the consuming application.");
    }
}
