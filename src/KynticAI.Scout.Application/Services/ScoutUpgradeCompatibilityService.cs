using System.Security.Cryptography;
using System.Text;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.Application.Services;

public sealed class ScoutUpgradeCompatibilityService(IScoutDbContext dbContext)
    : IScoutUpgradeCompatibilityService
{
    public async Task<ScoutUpgradeManifestV1> BuildManifestAsync(
        string tenantSlug,
        IReadOnlySet<string>? targetSupportedConnectorTypes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug))
            throw new ArgumentException("Tenant slug is required.", nameof(tenantSlug));

        var normalizedSlug = tenantSlug.Trim().ToLowerInvariant();
        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Slug == normalizedSlug, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{normalizedSlug}' was not found.");

        var installations = await dbContext.ConnectorInstallations
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderBy(x => x.ConnectorType)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var dataSourceIds = installations.Select(x => x.DataSourceId).Distinct().ToArray();
        var dataSources = await dbContext.DataSources
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && dataSourceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        // Secret values are never loaded for an upgrade manifest. Only stable local references
        // are required to decide whether the already-configured connector can continue in place.
        var credentialReferences = await dbContext.ConnectorCredentials
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && dataSourceIds.Contains(x.DataSourceId))
            .Select(x => new { x.DataSourceId, x.SecretReference })
            .ToListAsync(cancellationToken);

        var checkpoints = await dbContext.ConnectorCaptureCheckpoints
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .ToDictionaryAsync(x => x.ConnectorInstallationId, cancellationToken);

        var totalEventCount = await dbContext.SourceSystemEvents
            .AsNoTracking()
            .LongCountAsync(x => x.TenantId == tenant.Id, cancellationToken);
        var earliestEvent = totalEventCount == 0
            ? null
            : await dbContext.SourceSystemEvents
                .AsNoTracking()
                .Where(x => x.TenantId == tenant.Id)
                .MinAsync(x => (DateTime?)x.ObservedAtUtc, cancellationToken);
        var latestEvent = totalEventCount == 0
            ? null
            : await dbContext.SourceSystemEvents
                .AsNoTracking()
                .Where(x => x.TenantId == tenant.Id)
                .MaxAsync(x => (DateTime?)x.ObservedAtUtc, cancellationToken);

        var storageProvider = dbContext.Database.ProviderName ?? "unknown";
        var isPostgres = storageProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);
        var supportedProvider = isPostgres
            || storageProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        var descriptors = new List<ScoutConnectorUpgradeDescriptorV1>(installations.Count);
        var allTargetSupported = true;
        var allCredentialReferencesPresent = true;
        var allConnectorsHaveCompletedWholeSourceCapture = installations.Count > 0;
        var allConnectorHistoryIsExactFromDeclaredBoundary = installations.Count > 0;

        foreach (var installation in installations)
        {
            dataSources.TryGetValue(installation.DataSourceId, out var dataSource);
            checkpoints.TryGetValue(installation.Id, out var checkpoint);

            var connectorEventQuery = dbContext.SourceSystemEvents
                .AsNoTracking()
                .Where(x => x.TenantId == tenant.Id
                    && (x.DataSourceId == installation.DataSourceId
                        || (x.DataSourceId == null && x.SourceSystem == installation.ConnectorType)));
            var connectorEventCount = await connectorEventQuery.LongCountAsync(cancellationToken);
            var connectorEarliest = connectorEventCount == 0
                ? null
                : await connectorEventQuery.MinAsync(x => (DateTime?)x.ObservedAtUtc, cancellationToken);
            var connectorLatest = connectorEventCount == 0
                ? null
                : await connectorEventQuery.MaxAsync(x => (DateTime?)x.ObservedAtUtc, cancellationToken);

            var refs = credentialReferences
                .Where(x => x.DataSourceId == installation.DataSourceId)
                .Select(x => x.SecretReference)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            if (refs.Length == 0)
                allCredentialReferencesPresent = false;

            var targetSupported = targetSupportedConnectorTypes is null
                || targetSupportedConnectorTypes.Contains(installation.ConnectorType);
            allTargetSupported &= targetSupported;

            var hasCompletedWholeSourceCapture = checkpoint is not null
                && checkpoint.LastFullSourceCompletedAtUtc.HasValue
                && string.Equals(checkpoint.CoverageScope, LocalDataPlaneContracts.CoverageFullSource, StringComparison.Ordinal)
                && checkpoint.CapturedRecordCount > 0;
            var exactHistoryFromDeclaredBoundary = hasCompletedWholeSourceCapture
                && IsExactHistory(checkpoint!.HistoryCompleteness);
            var fullPermittedProfile = checkpoint is not null
                && string.Equals(checkpoint.CaptureProfile, LocalDataPlaneContracts.CaptureProfileFullPermittedV1, StringComparison.Ordinal);

            allConnectorsHaveCompletedWholeSourceCapture &= hasCompletedWholeSourceCapture;
            allConnectorHistoryIsExactFromDeclaredBoundary &= exactHistoryFromDeclaredBoundary;

            var warnings = new List<string>();
            if (!targetSupported)
                warnings.Add("Target tier does not declare this connector type as reusable.");
            if (refs.Length == 0)
                warnings.Add("No stable local credential reference was found; reconnect may be required.");
            if (checkpoint is null)
                warnings.Add("No whole-source capture checkpoint exists. Subject-on-demand Scout reads do not prove estate-wide upgrade coverage.");
            else
            {
                if (!checkpoint.LastFullSourceCompletedAtUtc.HasValue)
                    warnings.Add("Whole-source capture has not completed a generation yet.");
                if (!IsExactHistory(checkpoint.HistoryCompleteness))
                    warnings.Add($"Connector history is '{checkpoint.HistoryCompleteness}'. Fortress may rebuild current state, but exact pre-boundary history must not be claimed.");
                if (checkpoint.EarliestAvailableAtUtc.HasValue)
                    warnings.Add($"Earliest declared source-history boundary is {checkpoint.EarliestAvailableAtUtc.Value:O}.");
                if (!string.IsNullOrWhiteSpace(checkpoint.LastError))
                    warnings.Add("The most recent whole-source capture checkpoint records an error; inspect locally before cutover.");
            }
            if (dataSource is null)
                warnings.Add("Connector data source record is missing.");

            descriptors.Add(new ScoutConnectorUpgradeDescriptorV1(
                installation.Id,
                installation.DataSourceId,
                installation.WorkspaceId,
                installation.ConnectorType,
                installation.Status.ToString(),
                Sha256(dataSource?.ConnectionConfigJson ?? "{}"),
                refs,
                connectorEventCount,
                connectorEarliest,
                connectorLatest,
                hasCompletedWholeSourceCapture,
                hasCompletedWholeSourceCapture,
                fullPermittedProfile,
                checkpoint is null ? [] : [checkpoint.CaptureProfile],
                Array.Empty<string>(),
                warnings,
                checkpoint?.CoverageScope ?? LocalDataPlaneContracts.HistoryUnknown,
                checkpoint?.HistoryCompleteness ?? LocalDataPlaneContracts.HistoryUnknown,
                checkpoint?.EarliestAvailableAtUtc ?? checkpoint?.EarliestCapturedAtUtc,
                checkpoint?.LastFullSourceCompletedAtUtc,
                checkpoint?.Generation ?? 0,
                checkpoint is null ? string.Empty : Sha256(checkpoint.HighWaterMarkJson)));
        }

        // The preflight is intentionally derived from bounded checkpoint/aggregate state. It no
        // longer loads every PayloadJson/HeadersJson into memory, so a million retained source
        // events do not turn a compatibility check into a million-row application scan.
        var evidence = new UpgradeCompatibilityEvidence(
            supportedProvider,
            isPostgres,
            installations.Count > 0,
            allCredentialReferencesPresent,
            totalEventCount > 0,
            allConnectorsHaveCompletedWholeSourceCapture,
            allConnectorsHaveCompletedWholeSourceCapture,
            allTargetSupported,
            RequiresSourceReconnect: false,
            HistoricalCoverageKnownComplete: allConnectorHistoryIsExactFromDeclaredBoundary);
        var readiness = ScoutFortressUpgradePolicy.Classify(evidence);

        var reasons = BuildReasons(readiness, storageProvider, evidence, descriptors);
        var requiredActions = BuildActions(readiness, isPostgres);

        return new ScoutUpgradeManifestV1(
            LocalDataPlaneContracts.UpgradeManifestV1,
            tenant.Id,
            tenant.Slug,
            DateTime.UtcNow,
            storageProvider,
            CustomerDataRemainsLocal: true,
            ContainsCredentials: false,
            totalEventCount,
            earliestEvent,
            latestEvent,
            descriptors,
            readiness,
            reasons,
            requiredActions,
            "This manifest contains topology, hashes, local secret references and bounded capture-coverage metadata only. Raw source payloads, source high-water positions, protected credential values, customer identifiers, context facts and model inputs remain in the customer data plane.");
    }

    private static bool IsExactHistory(string historyCompleteness)
        => string.Equals(historyCompleteness, LocalDataPlaneContracts.HistoryComplete, StringComparison.Ordinal)
            || string.Equals(historyCompleteness, LocalDataPlaneContracts.HistoryFromRetentionBoundary, StringComparison.Ordinal);

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IReadOnlyList<string> BuildReasons(
        LocalUpgradeReadiness readiness,
        string storageProvider,
        UpgradeCompatibilityEvidence evidence,
        IReadOnlyList<ScoutConnectorUpgradeDescriptorV1> connectors)
    {
        var reasons = new List<string> { $"Local Scout provider: {storageProvider}." };
        if (evidence.IsPostgres)
            reasons.Add("PostgreSQL can be retained as the customer-local relational substrate during an additive Fortress upgrade.");
        if (!evidence.AllRetainedEventsHaveCaptureMetadata)
            reasons.Add("At least one connector has not completed a FULL_SOURCE capture generation; on-demand selector history alone is not a lossless estate journal.");
        if (!evidence.AllRetainedEventsRetainFullPermittedPayload)
            reasons.Add("At least one connector cannot prove a completed full customer-permitted source capture.");
        if (!evidence.HistoricalCoverageKnownComplete)
        {
            var boundaries = connectors
                .Where(x => x.EarliestUpgradeCompatibleAtUtc.HasValue)
                .Select(x => $"{x.ConnectorType}={x.EarliestUpgradeCompatibleAtUtc:O}")
                .ToArray();
            reasons.Add(boundaries.Length == 0
                ? "Exact source-history coverage does not yet have a proven boundary."
                : $"Exact history is bounded by connector-declared coverage: {string.Join(", ", boundaries)}.");
        }
        if (!evidence.ConnectorCredentialsReferencedLocally)
            reasons.Add("One or more connector installations do not have a reusable local credential reference.");
        if (!evidence.ConnectorTypesSupportedByTarget)
            reasons.Add("One or more connector types are not declared compatible by the target tier.");
        reasons.Add($"Upgrade classification: {readiness}.");
        return reasons;
    }

    private static IReadOnlyList<string> BuildActions(LocalUpgradeReadiness readiness, bool isPostgres)
    {
        var actions = new List<string>();
        if (!isPostgres)
            actions.Add("Migrate the local Scout relational store to PostgreSQL before claiming a same-database Fortress upgrade.");
        actions.Add("Complete and verify a whole-source connector capture generation before the upgrade barrier.");
        actions.Add("Take a customer-local database backup/snapshot before the upgrade barrier.");
        actions.Add("Pause connector leases at a recorded local source high-water mark before switching capture ownership.");
        actions.Add("Install Fortress additively; do not delete Scout source-event, connector, credential-reference or checkpoint state during the rebuild.");
        actions.Add("Backfill Fortress governed state locally from the retained Scout source journal before resuming connector leases.");
        actions.Add("Verify connector IDs, local credential references, high-water hashes, source-event counts and deterministic hashes before finalising cutover.");
        if (readiness == LocalUpgradeReadiness.HistoryLimited)
            actions.Add("Explain the earliest provable historical boundary to the customer; do not fabricate pre-boundary Fortress history.");
        if (readiness is LocalUpgradeReadiness.ReconnectRequired or LocalUpgradeReadiness.Unsupported)
            actions.Add("Stop automatic cutover and require an explicit operator/customer migration decision.");
        return actions;
    }
}
