using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

        // Only secret references are used in the manifest. ProtectedValue and SecretKey never leave
        // the local data plane through this compatibility service.
        var credentialReferences = await dbContext.ConnectorCredentials
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && dataSourceIds.Contains(x.DataSourceId))
            .Select(x => new { x.DataSourceId, x.SecretReference })
            .ToListAsync(cancellationToken);

        var events = await dbContext.SourceSystemEvents
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .Select(x => new EventProjection(
                x.DataSourceId,
                x.SourceSystem,
                x.ObservedAtUtc,
                x.PayloadJson,
                x.HeadersJson))
            .ToListAsync(cancellationToken);

        var storageProvider = dbContext.Database.ProviderName ?? "unknown";
        var isPostgres = storageProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);
        var supportedProvider = isPostgres
            || storageProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        var descriptors = new List<ScoutConnectorUpgradeDescriptorV1>(installations.Count);
        var allTargetSupported = true;
        var allCredentialReferencesPresent = true;
        var anyMissingCaptureMetadata = false;
        var anyMissingFullPayload = false;

        foreach (var installation in installations)
        {
            dataSources.TryGetValue(installation.DataSourceId, out var dataSource);
            var connectorEvents = events
                .Where(x => x.DataSourceId == installation.DataSourceId
                    || (x.DataSourceId is null
                        && string.Equals(x.SourceSystem, installation.ConnectorType, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(x => x.ObservedAtUtc)
                .ToList();

            var parsedCapture = connectorEvents
                .Select(x => TryReadCaptureMetadata(x.HeadersJson))
                .ToList();
            var compatibleCapture = parsedCapture.Where(x => x is { IsUpgradeCompatible: true }).Cast<LocalSourceCaptureMetadataV1>().ToList();
            var allHaveCapture = connectorEvents.Count == 0 || compatibleCapture.Count == connectorEvents.Count;
            var fullPermitted = connectorEvents.Count == 0
                || (compatibleCapture.Count == connectorEvents.Count
                    && compatibleCapture.All(x => x.FullPermittedPayloadRetained)
                    && connectorEvents.All(x => HasNonEmptyPayload(x.PayloadJson)));

            if (!allHaveCapture)
                anyMissingCaptureMetadata = true;
            if (!fullPermitted)
                anyMissingFullPayload = true;

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

            var warnings = new List<string>();
            if (!targetSupported)
                warnings.Add("Target tier does not declare this connector type as reusable.");
            if (refs.Length == 0)
                warnings.Add("No stable local credential reference was found; reconnect may be required.");
            if (!allHaveCapture && connectorEvents.Count > 0)
                warnings.Add("Some retained events pre-date the upgrade-compatible capture contract.");
            if (!fullPermitted && connectorEvents.Count > 0)
                warnings.Add("Some retained events cannot prove that the full customer-permitted payload was retained.");
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
                connectorEvents.Count,
                connectorEvents.FirstOrDefault()?.ObservedAtUtc,
                connectorEvents.LastOrDefault()?.ObservedAtUtc,
                compatibleCapture.Count > 0,
                allHaveCapture,
                fullPermitted,
                compatibleCapture.Select(x => x.CaptureProfile).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray(),
                compatibleCapture.Select(x => x.SchemaFingerprintSha256).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray(),
                warnings));
        }

        var allEventsHaveMetadata = events.Count == 0 || !anyMissingCaptureMetadata;
        var allEventsRetainFullPermitted = events.Count == 0 || !anyMissingFullPayload;
        var historicalCoverageKnownComplete = events.Count == 0
            || (allEventsHaveMetadata && compatibleCoverageStartsAtFirstRetainedEvent(events));

        var evidence = new UpgradeCompatibilityEvidence(
            supportedProvider,
            isPostgres,
            installations.Count > 0,
            allCredentialReferencesPresent,
            events.Count > 0,
            allEventsHaveMetadata,
            allEventsRetainFullPermitted,
            allTargetSupported,
            RequiresSourceReconnect: false,
            historicalCoverageKnownComplete);
        var readiness = ScoutFortressUpgradePolicy.Classify(evidence);

        var reasons = BuildReasons(readiness, storageProvider, evidence);
        var requiredActions = BuildActions(readiness, isPostgres);

        return new ScoutUpgradeManifestV1(
            LocalDataPlaneContracts.UpgradeManifestV1,
            tenant.Id,
            tenant.Slug,
            DateTime.UtcNow,
            storageProvider,
            CustomerDataRemainsLocal: true,
            ContainsCredentials: false,
            events.Count,
            events.Count == 0 ? null : events.Min(x => x.ObservedAtUtc),
            events.Count == 0 ? null : events.Max(x => x.ObservedAtUtc),
            descriptors,
            readiness,
            reasons,
            requiredActions,
            "This manifest contains topology, hashes, local secret references and coverage metadata only. Raw source payloads, protected credential values, customer identifiers, context facts and model inputs remain in the customer data plane.");
    }

    private static LocalSourceCaptureMetadataV1? TryReadCaptureMetadata(string headersJson)
    {
        if (string.IsNullOrWhiteSpace(headersJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(headersJson);
            if (!document.RootElement.TryGetProperty("kynticCapture", out var capture))
                return null;
            return capture.Deserialize<LocalSourceCaptureMetadataV1>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasNonEmptyPayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return false;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Object => document.RootElement.EnumerateObject().Any(),
                JsonValueKind.Array => document.RootElement.GetArrayLength() > 0,
                JsonValueKind.Null or JsonValueKind.Undefined => false,
                _ => true
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool compatibleCoverageStartsAtFirstRetainedEvent(IReadOnlyList<EventProjection> events)
    {
        var first = events.OrderBy(x => x.ObservedAtUtc).FirstOrDefault();
        return first is not null && TryReadCaptureMetadata(first.HeadersJson) is { IsUpgradeCompatible: true };
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IReadOnlyList<string> BuildReasons(
        LocalUpgradeReadiness readiness,
        string storageProvider,
        UpgradeCompatibilityEvidence evidence)
    {
        var reasons = new List<string> { $"Local Scout provider: {storageProvider}." };
        if (evidence.IsPostgres)
            reasons.Add("PostgreSQL can be retained as the customer-local relational substrate during an additive Fortress upgrade.");
        if (!evidence.AllRetainedEventsHaveCaptureMetadata)
            reasons.Add("At least one retained source event lacks the v1 upgrade-capture metadata required to prove an exact source position/capture policy.");
        if (!evidence.AllRetainedEventsRetainFullPermittedPayload)
            reasons.Add("At least one retained source event cannot prove complete customer-permitted capture.");
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
        actions.Add("Take a customer-local database backup/snapshot before the upgrade barrier.");
        actions.Add("Pause connector leases at a recorded source high-water mark before switching consumers.");
        actions.Add("Install Fortress additively; do not delete Scout source-event or connector state during the rebuild.");
        actions.Add("Backfill Fortress governed state locally from the retained Scout source journal before resuming connector leases.");
        actions.Add("Verify connector IDs, local credential references, high-water marks, source-event counts and deterministic hashes before finalising cutover.");
        if (readiness == LocalUpgradeReadiness.HistoryLimited)
            actions.Add("Explain the earliest provable historical boundary to the customer; do not fabricate pre-boundary Fortress history.");
        if (readiness is LocalUpgradeReadiness.ReconnectRequired or LocalUpgradeReadiness.Unsupported)
            actions.Add("Stop automatic cutover and require an explicit operator/customer migration decision.");
        return actions;
    }

    private sealed record EventProjection(
        Guid? DataSourceId,
        string SourceSystem,
        DateTime ObservedAtUtc,
        string PayloadJson,
        string HeadersJson);
}
