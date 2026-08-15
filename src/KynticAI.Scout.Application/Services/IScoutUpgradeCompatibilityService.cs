using KynticAI.Scout.Application.Contracts;

namespace KynticAI.Scout.Application.Services;

public interface IScoutUpgradeCompatibilityService
{
    Task<ScoutUpgradeManifestV1> BuildManifestAsync(
        string tenantSlug,
        IReadOnlySet<string>? targetSupportedConnectorTypes,
        CancellationToken cancellationToken);
}
