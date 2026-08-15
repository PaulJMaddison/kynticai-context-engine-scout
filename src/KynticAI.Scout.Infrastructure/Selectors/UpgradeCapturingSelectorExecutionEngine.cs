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
        await sourceCaptureJournal.CaptureSelectorOutcomeAsync(
            runtimeContext,
            userProfile,
            mode,
            outcome,
            cancellationToken);
        return outcome;
    }

    public Task<SelectorPipelineOutcome> ValidateAsync(
        SelectorRuntimeContext runtimeContext,
        UserProfile? userProfile,
        CancellationToken cancellationToken)
        => inner.ValidateAsync(runtimeContext, userProfile, cancellationToken);

    public async Task<IReadOnlyList<SelectorPipelineOutcome>> ExecuteSelectorsAsync(
        IReadOnlyList<SelectorRuntimeContext> runtimeContexts,
        UserProfile userProfile,
        SelectorExecutionMode mode,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<SelectorPipelineOutcome>(runtimeContexts.Count);
        foreach (var runtimeContext in runtimeContexts)
        {
            outcomes.Add(await ExecuteAsync(runtimeContext, userProfile, mode, cancellationToken));
        }
        return outcomes;
    }
}
