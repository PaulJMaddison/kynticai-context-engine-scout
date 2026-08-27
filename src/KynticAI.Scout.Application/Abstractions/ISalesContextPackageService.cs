using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Entities;

namespace KynticAI.Scout.Application.Abstractions;

/// <summary>
/// Legacy sales-context compatibility contract.
///
/// The core contract packages already-derived, source-traced context only. It
/// does not define sales heuristics, prompts, model providers or inference.
/// </summary>
public interface ISalesContextPackageService
{
    SalesContextPackageResult BuildContextPackage(
        Tenant tenant,
        UserProfile userProfile,
        ContextSnapshot contextSnapshot,
        string salesObjective,
        DateTime utcNow);
}
