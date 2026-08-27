using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;

namespace KynticAI.Scout.Infrastructure.ReferenceData;

/// <summary>
/// Production-safe default. Scout does not own or require a second operational
/// database; customer systems are accessed through configured connectors.
/// </summary>
internal sealed class NullOperationalReferenceDataProvider : IOperationalReferenceDataProvider
{
    public bool IsEnabled => false;

    public Task<OperationalAccountReferenceResult?> GetAccountAsync(
        string tenantSlug,
        string externalAccountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<OperationalAccountReferenceResult?>(null);
    }

    public Task<string?> ResolveExternalUserIdByAccountAsync(
        string tenantSlug,
        string externalAccountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }

    public Task<OperationalSourceSummaryResult?> GetSourceSummaryAsync(
        string tenantSlug,
        string externalUserId,
        bool canViewSensitivePii,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<OperationalSourceSummaryResult?>(null);
    }
}
