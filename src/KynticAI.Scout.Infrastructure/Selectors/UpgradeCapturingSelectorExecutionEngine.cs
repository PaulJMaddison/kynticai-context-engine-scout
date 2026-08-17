using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Connectors;

namespace KynticAI.Scout.Infrastructure.Selectors;

/// <summary>
/// Decorates the existing selector engine so a live source read is durably retained in the
/// customer-local source journal before Scout treats the selector result as derived context.
/// Preview and dry-run calls remain non-persistent.
/// </summary>
internal sealed class UpgradeCapturingSelectorExecutionEngine(
    SelectorExecutionEngine inner,
    LocalSourceCaptureJournal sourceCaptureJournal)
    : ISelectorExecutionEngine
{
    public async Task<SelectorPipelineOutcome> ExecuteAsync(
        SelectorRuntimeContext runtimeContext,
        UserProfile userProfile,
        SelectorExecutionMode mode,
        CancellationToken cancellationToken)
    {
        var outcome = await inner.ExecuteAsync(runtimeContext, userProfile, mode, cancellationToken);
        // The inner engine converts ordinary connector exceptions into a failed pipeline outcome.
        // Cancellation is different: shutdown/request abort must remain cancellation so callers do
        // not persist a false business failure or source-capture record for interrupted work.
        cancellationToken.ThrowIfCancellationRequested();
        await sourceCaptureJournal.CaptureSelectorOutcomeAsync(
            runtimeContext,
            userProfile,
            mode,
            outcome,
            cancellationToken);
        return outcome;
    }

    public async Task<SelectorPipelineOutcome> ValidateAsync(
        SelectorRuntimeContext runtimeContext,
        UserProfile? userProfile,
        CancellationToken cancellationToken)
    {
        var outcome = await inner.ValidateAsync(runtimeContext, userProfile, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return outcome;
    }

    public async Task<IReadOnlyList<SelectorPipelineOutcome>> ExecuteSelectorsAsync(
        IReadOnlyList<SelectorRuntimeContext> runtimeContexts,
        UserProfile userProfile,
        SelectorExecutionMode mode,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<SelectorPipelineOutcome>(runtimeContexts.Count);
        foreach (var runtimeContext in runtimeContexts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outcomes.Add(await ExecuteAsync(runtimeContext, userProfile, mode, cancellationToken));
        }
        return outcomes;
    }
}
