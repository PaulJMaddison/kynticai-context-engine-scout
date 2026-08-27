using KynticAI.Scout.Application.Contracts;

namespace KynticAI.Scout.Application.Abstractions;

/// <summary>
/// Optional operational/reference data used by the fictional Scout demo experience.
/// Production Scout must not require this provider: real customer operational data
/// reaches Scout through connectors and source-event ingestion.
/// </summary>
public interface IOperationalReferenceDataProvider
{
    bool IsEnabled { get; }

    Task<OperationalAccountReferenceResult?> GetAccountAsync(
        string tenantSlug,
        string externalAccountId,
        CancellationToken cancellationToken);

    Task<string?> ResolveExternalUserIdByAccountAsync(
        string tenantSlug,
        string externalAccountId,
        CancellationToken cancellationToken);

    Task<OperationalSourceSummaryResult?> GetSourceSummaryAsync(
        string tenantSlug,
        string externalUserId,
        bool canViewSensitivePii,
        CancellationToken cancellationToken);
}

public sealed record OperationalContactReferenceResult(
    string ExternalUserId,
    string FullName,
    string Email,
    string JobTitle);

public sealed record OperationalAccountReferenceResult(
    string ExternalAccountId,
    string AccountName,
    string Domain,
    string Industry,
    string Segment,
    string Region,
    string LifecycleStage,
    IReadOnlyList<OperationalContactReferenceResult> Contacts);
