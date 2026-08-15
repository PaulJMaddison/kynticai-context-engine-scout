using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KynticAI.Scout.Infrastructure.Connectors;

/// <summary>
/// Runs whole-source capture connectors into Scout's existing local SourceSystemEvent journal.
/// This is the path required for upgrade-compatible source continuity. Selector reads are useful
/// evidence but do not prove estate-wide coverage.
///
/// Capture ownership is leased in the customer-local database. During a tier cutover the same
/// lease/checkpoint is the barrier that prevents Scout/Fortress from independently polling the
/// same source.
///
/// SourceNamespace is deliberately tier-neutral and connector-installation scoped. Connector
/// type alone is not unique: two different CRMs can both contain contact/123. Preserving the
/// connector installation GUID gives Scout backfill and later Fortress live capture the same
/// exact source namespace without false exact-key collisions.
/// </summary>
internal sealed class FullSourceCaptureCoordinator(
    ScoutDbContext dbContext,
    IConnectorCredentialStore credentialStore,
    IEnumerable<IUpgradeSourceCaptureConnector> captureConnectors,
    IClock clock,
    ILogger<FullSourceCaptureCoordinator> logger)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    private readonly string owner = $"scout:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private readonly IReadOnlyDictionary<string, IUpgradeSourceCaptureConnector> connectors = captureConnectors
        .GroupBy(x => x.ConnectorType, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<FullSourceCaptureRunResult>> RunAllOnceAsync(
        int maxRecordsPerConnector,
        CancellationToken cancellationToken)
    {
        maxRecordsPerConnector = Math.Clamp(maxRecordsPerConnector, 1, 5_000);
        var installations = await dbContext.ConnectorInstallations
            .AsNoTracking()
            .OrderBy(x => x.TenantId)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var dataSourceIds = installations.Select(x => x.DataSourceId).Distinct().ToArray();
        var dataSources = await dbContext.DataSources
            .AsNoTracking()
            .Where(x => dataSourceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var results = new List<FullSourceCaptureRunResult>(installations.Count);
        foreach (var installation in installations)
        {
            if (!dataSources.TryGetValue(installation.DataSourceId, out var dataSource))
            {
                results.Add(new FullSourceCaptureRunResult(
                    installation.Id, installation.ConnectorType, false, 0, false,
                    "Data source is missing."));
                continue;
            }

            if (!connectors.TryGetValue(installation.ConnectorType, out var connector))
            {
                results.Add(new FullSourceCaptureRunResult(
                    installation.Id, installation.ConnectorType, false, 0, false,
                    "Connector has no IUpgradeSourceCaptureConnector implementation; full-source continuity is not claimed."));
                continue;
            }

            results.Add(await RunInstallationOnceAsync(
                installation,
                dataSource,
                connector,
                maxRecordsPerConnector,
                cancellationToken));
        }

        return results;
    }

    private async Task<FullSourceCaptureRunResult> RunInstallationOnceAsync(
        ConnectorInstallation installation,
        DataSource dataSource,
        IUpgradeSourceCaptureConnector connector,
        int maxRecords,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var checkpoint = await dbContext.ConnectorCaptureCheckpoints
            .SingleOrDefaultAsync(x => x.TenantId == installation.TenantId
                && x.ConnectorInstallationId == installation.Id,
                cancellationToken);
        if (checkpoint is null)
        {
            checkpoint = ConnectorCaptureCheckpoint.Create(
                installation.TenantId,
                installation.Id,
                installation.DataSourceId,
                LocalDataPlaneContracts.CaptureProfileFullPermittedV1,
                "1",
                LocalDataPlaneContracts.CoverageFullSource,
                LocalDataPlaneContracts.HistoryUnknown,
                null,
                now);
            dbContext.ConnectorCaptureCheckpoints.Add(checkpoint);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (!checkpoint.TryAcquireLease(owner, LeaseDuration, now))
        {
            return new FullSourceCaptureRunResult(
                installation.Id, installation.ConnectorType, false, 0, false,
                "Capture lease is held by another local worker.");
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.Entry(checkpoint).State = EntityState.Detached;
            return new FullSourceCaptureRunResult(
                installation.Id, installation.ConnectorType, false, 0, false,
                "Capture lease was acquired concurrently by another local worker.");
        }

        try
        {
            var configuration = ParseConfiguration(dataSource.ConnectionConfigJson);
            ValidateCapturePermission(connector.ConnectorType, configuration);

            var resolved = await credentialStore.ResolveConfigurationSecretsAsync(
                dataSource.TenantId,
                configuration,
                cancellationToken);
            var credentials = resolved["credentials"] as JsonObject ?? new JsonObject();
            var continuationBefore = checkpoint.ContinuationToken;
            var historyBefore = checkpoint.HistoryCompleteness;
            var consistencyBefore = checkpoint.CurrentStateConsistency;

            var batch = await connector.CaptureBatchAsync(
                new ConnectorSourceCaptureRequest(
                    installation,
                    dataSource,
                    resolved,
                    credentials,
                    continuationBefore,
                    maxRecords,
                    now),
                cancellationToken);

            checkpoint.RenewLease(owner, LeaseDuration, clock.UtcNow);

            var batchHistory = ValidateBatchSemantics(
                connector.ConnectorType,
                batch.Records,
                continuationBefore,
                historyBefore,
                batch.CurrentStateConsistency,
                consistencyBefore);
            var earliestAvailable = batch.Records
                .Where(x => x.EarliestAvailableAtUtc.HasValue)
                .Select(x => x.EarliestAvailableAtUtc)
                .Min();
            if (batch.Records.Count > 0)
            {
                checkpoint.ObserveCaptureSemantics(
                    owner,
                    batchHistory,
                    earliestAvailable,
                    clock.UtcNow,
                    batch.CurrentStateConsistency);
            }

            var persisted = 0;
            DateTime? earliest = null;
            DateTime? latest = null;
            foreach (var record in batch.Records)
            {
                ValidateRecord(connector.ConnectorType, record);
                if (await PersistRecordAsync(installation, dataSource, record, cancellationToken))
                {
                    persisted++;
                }
                earliest = Min(earliest, record.OccurredAtUtc);
                latest = Max(latest, record.OccurredAtUtc);
            }

            checkpoint.Advance(
                owner,
                batch.NextContinuationToken,
                batch.HighWaterMarkJson,
                persisted,
                earliest,
                latest,
                clock.UtcNow);
            if (batch.IsComplete)
            {
                checkpoint.CompleteFullSourceGeneration(
                    owner,
                    batch.HighWaterMarkJson,
                    checkpoint.HistoryCompleteness,
                    clock.UtcNow,
                    LocalDataPlaneContracts.PayloadStorageExactTextV1);
            }
            checkpoint.ReleaseLease(owner, clock.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);

            return new FullSourceCaptureRunResult(
                installation.Id,
                installation.ConnectorType,
                true,
                persisted,
                batch.IsComplete,
                null);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Whole-source capture failed for connector installation {ConnectorInstallationId}.",
                installation.Id);
            try
            {
                checkpoint.MarkFailed(owner, exception.Message, clock.UtcNow);
                checkpoint.ReleaseLease(owner, clock.UtcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception stateException)
            {
                logger.LogError(stateException,
                    "Failed to persist capture failure state for connector installation {ConnectorInstallationId}.",
                    installation.Id);
            }
            return new FullSourceCaptureRunResult(
                installation.Id,
                installation.ConnectorType,
                false,
                0,
                false,
                exception.Message);
        }
    }

    private async Task<bool> PersistRecordAsync(
        ConnectorInstallation installation,
        DataSource dataSource,
        ConnectorSourceCaptureRecord record,
        CancellationToken cancellationToken)
    {
        var eventId = $"capture:{record.IdempotencyKey}";
        var existing = await dbContext.SourceSystemEvents
            .SingleOrDefaultAsync(x => x.TenantId == installation.TenantId
                && x.SourceSystem == installation.ConnectorType
                && x.EventId == eventId,
                cancellationToken);
        if (existing is not null)
        {
            await RepairExactEvidenceForDuplicateAsync(
                installation,
                dataSource,
                existing,
                record,
                cancellationToken);
            return false;
        }

        var sourceNamespace = BuildSourceNamespace(installation.Id);
        var capture = new LocalSourceCaptureMetadataV1(
            LocalDataPlaneContracts.CaptureMetadataV1,
            installation.Id,
            installation.ConnectorType,
            $"{installation.ConnectorType}.full-source.v1",
            record.CaptureProfile,
            record.CaptureProfileVersion,
            sourceNamespace,
            record.SourceObjectType,
            record.SourceRecordId,
            record.Operation,
            record.SourcePositionJson,
            EnsureUtc(record.OccurredAtUtc),
            record.SourceRecordedAtUtc is null ? null : EnsureUtc(record.SourceRecordedAtUtc.Value),
            clock.UtcNow,
            record.SchemaFingerprintSha256,
            record.RedactionPolicyVersion,
            true,
            record.IdempotencyKey,
            LocalDataPlaneContracts.CoverageFullSource,
            record.HistoryCompleteness,
            record.EarliestAvailableAtUtc,
            record.RawPayloadSha256,
            record.PermittedFieldSetSha256,
            LocalDataPlaneContracts.PayloadStorageUnknown);
        if (!capture.HasStructurallyValidCaptureMetadata)
        {
            throw new InvalidOperationException("Whole-source connector returned incomplete capture metadata.");
        }

        var headersJson = JsonSerializer.Serialize(new
        {
            kynticCapture = capture,
            origin = "full-source-capture",
            dataSourceId = dataSource.Id
        });
        var sourceEvent = SourceSystemEvent.Create(
            installation.TenantId,
            installation.WorkspaceId,
            eventId,
            installation.ConnectorType,
            $"capture.{record.SourceObjectType}.{record.Operation}",
            null,
            null,
            null,
            dataSource.Id,
            record.RawPayloadJson,
            headersJson,
            record.IdempotencyKey.Length >= 32
                ? record.IdempotencyKey[..32]
                : record.IdempotencyKey,
            EnsureUtc(record.OccurredAtUtc),
            clock.UtcNow);
        sourceEvent.MarkProcessed(
            0,
            "Whole-source customer-permitted payload retained for local tier continuity.",
            clock.UtcNow);
        dbContext.SourceSystemEvents.Add(sourceEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// A deterministic recapture may encounter a SourceSystemEvent that was written by the
    /// earlier jsonb-only continuity code before exact payload evidence existed. Do not insert a
    /// duplicate and do not hash the jsonb round-trip text. The recaptured connector payload is
    /// the exact evidence for the same idempotency key; attach it to the existing event locally.
    /// </summary>
    private async Task RepairExactEvidenceForDuplicateAsync(
        ConnectorInstallation installation,
        DataSource dataSource,
        SourceSystemEvent existing,
        ConnectorSourceCaptureRecord record,
        CancellationToken cancellationToken)
    {
        var actualHash = Sha256(record.RawPayloadJson);
        if (!string.Equals(actualHash, record.RawPayloadSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Deterministic recapture payload does not match its declared raw payload hash.");
        }

        var evidence = await dbContext.SourceCapturePayloadEvidenceRecords
            .SingleOrDefaultAsync(x => x.TenantId == installation.TenantId
                && x.SourceSystemEventId == existing.Id,
                cancellationToken);
        if (evidence is not null)
        {
            if (!string.Equals(evidence.StorageContract, LocalDataPlaneContracts.PayloadStorageExactTextV1, StringComparison.Ordinal)
                || !string.Equals(evidence.RawPayloadSha256, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Existing exact payload evidence contradicts the deterministic recapture; refusing to overwrite evidence.");
            }
            StampExistingCaptureExact(existing, installation, dataSource, record, actualHash);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        StampExistingCaptureExact(existing, installation, dataSource, record, actualHash);
        dbContext.SourceCapturePayloadEvidenceRecords.Add(SourceCapturePayloadEvidence.Create(
            installation.TenantId,
            existing.Id,
            installation.Id,
            LocalDataPlaneContracts.PayloadStorageExactTextV1,
            LocalDataPlaneContracts.CoverageFullSource,
            record.RawPayloadJson,
            actualHash,
            clock.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void StampExistingCaptureExact(
        SourceSystemEvent existing,
        ConnectorInstallation installation,
        DataSource dataSource,
        ConnectorSourceCaptureRecord record,
        string actualHash)
    {
        var headers = JsonNode.Parse(existing.HeadersJson) as JsonObject
            ?? throw new InvalidOperationException("Existing deterministic capture has invalid HeadersJson.");
        var capture = headers["kynticCapture"] as JsonObject
            ?? throw new InvalidOperationException("Existing deterministic capture has no kynticCapture envelope.");

        var contract = capture["Contract"]?.GetValue<string>();
        var connectorId = capture["ConnectorInstanceId"]?.GetValue<string>();
        var idempotency = capture["IdempotencyKey"]?.GetValue<string>();
        var rawHash = capture["RawPayloadSha256"]?.GetValue<string>();
        if (!string.Equals(contract, LocalDataPlaneContracts.CaptureMetadataV1, StringComparison.Ordinal)
            || !Guid.TryParse(connectorId, out var parsedConnectorId)
            || parsedConnectorId != installation.Id
            || !string.Equals(idempotency, record.IdempotencyKey, StringComparison.Ordinal)
            || !string.Equals(rawHash, actualHash, StringComparison.OrdinalIgnoreCase)
            || existing.DataSourceId != dataSource.Id)
        {
            throw new InvalidOperationException(
                "Existing capture metadata does not match the deterministic recapture; refusing evidence repair.");
        }

        capture["SourceNamespace"] = BuildSourceNamespace(installation.Id);
        capture["PayloadStorageContract"] = LocalDataPlaneContracts.PayloadStorageExactTextV1;
        dbContext.Entry(existing).Property(x => x.HeadersJson).CurrentValue = headers.ToJsonString();
    }

    private static string ValidateBatchSemantics(
        string connectorType,
        IReadOnlyList<ConnectorSourceCaptureRecord> records,
        string? continuationBefore,
        string historyBefore,
        string currentStateConsistency,
        string consistencyBefore)
    {
        ValidateCurrentStateConsistency(currentStateConsistency);
        if (!string.IsNullOrWhiteSpace(continuationBefore)
            && !string.IsNullOrWhiteSpace(consistencyBefore)
            && !string.Equals(consistencyBefore, LocalDataPlaneContracts.CurrentStateUnknown, StringComparison.Ordinal)
            && !string.Equals(consistencyBefore, currentStateConsistency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Whole-source capture changed current-state consistency semantics inside one paged generation.");
        }

        if (records.Count == 0)
        {
            return string.IsNullOrWhiteSpace(historyBefore)
                ? LocalDataPlaneContracts.HistoryUnknown
                : historyBefore;
        }

        var histories = records
            .Select(x => x.HistoryCompleteness)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (histories.Length != 1 || !IsKnownHistory(histories[0]))
        {
            throw new InvalidOperationException(
                "A whole-source capture page must use one known history-completeness value.");
        }

        var incomingHistory = histories[0];
        if (!string.IsNullOrWhiteSpace(continuationBefore)
            && !string.IsNullOrWhiteSpace(historyBefore)
            && !string.Equals(historyBefore, LocalDataPlaneContracts.HistoryUnknown, StringComparison.Ordinal)
            && !string.Equals(historyBefore, incomingHistory, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Whole-source capture changed history-completeness semantics inside one paged generation.");
        }

        if (IsGenericSnapshotConnector(connectorType)
            && !string.Equals(incomingHistory, LocalDataPlaneContracts.HistorySnapshotOnly, StringComparison.Ordinal)
            && !string.Equals(incomingHistory, LocalDataPlaneContracts.HistoryUnknown, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Generic connector '{connectorType}' may only claim SNAPSHOT_ONLY or UNKNOWN history. Use a provider-specific capture connector to prove historical completeness.");
        }

        return incomingHistory;
    }

    private static void ValidateCurrentStateConsistency(string value)
    {
        var known = string.Equals(value, LocalDataPlaneContracts.CurrentStateImmutableSnapshot, StringComparison.Ordinal)
            || string.Equals(value, LocalDataPlaneContracts.CurrentStatePointInTime, StringComparison.Ordinal)
            || string.Equals(value, LocalDataPlaneContracts.CurrentStateSourceNativeOrdered, StringComparison.Ordinal)
            || string.Equals(value, LocalDataPlaneContracts.CurrentStateLiveKeyset, StringComparison.Ordinal)
            || string.Equals(value, LocalDataPlaneContracts.CurrentStateApiCursor, StringComparison.Ordinal)
            || string.Equals(value, LocalDataPlaneContracts.CurrentStateUnknown, StringComparison.Ordinal);
        if (!known)
        {
            throw new InvalidOperationException(
                $"Unknown current-state consistency value '{value}'.");
        }
    }

    private static void ValidateRecord(string connectorType, ConnectorSourceCaptureRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.SourceObjectType)
            || string.IsNullOrWhiteSpace(record.SourceRecordId)
            || string.IsNullOrWhiteSpace(record.Operation)
            || string.IsNullOrWhiteSpace(record.SourcePositionJson)
            || string.IsNullOrWhiteSpace(record.RawPayloadJson)
            || string.IsNullOrWhiteSpace(record.IdempotencyKey)
            || string.IsNullOrWhiteSpace(record.RawPayloadSha256)
            || string.IsNullOrWhiteSpace(record.SchemaFingerprintSha256)
            || string.IsNullOrWhiteSpace(record.PermittedFieldSetSha256)
            || string.IsNullOrWhiteSpace(record.RedactionPolicyVersion)
            || string.IsNullOrWhiteSpace(record.CaptureProfileVersion))
        {
            throw new InvalidOperationException("Whole-source connector returned an incomplete source record.");
        }

        if (!string.Equals(record.CaptureProfile, LocalDataPlaneContracts.CaptureProfileFullPermittedV1, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Whole-source connector did not return the full-permitted capture profile.");
        }

        if (!IsKnownHistory(record.HistoryCompleteness))
        {
            throw new InvalidOperationException($"Unknown history-completeness value '{record.HistoryCompleteness}'.");
        }

        if (IsGenericSnapshotConnector(connectorType)
            && !string.Equals(record.HistoryCompleteness, LocalDataPlaneContracts.HistorySnapshotOnly, StringComparison.Ordinal)
            && !string.Equals(record.HistoryCompleteness, LocalDataPlaneContracts.HistoryUnknown, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Generic connector '{connectorType}' cannot claim exact historical coverage.");
        }

        if (string.Equals(record.HistoryCompleteness, LocalDataPlaneContracts.HistoryFromRetentionBoundary, StringComparison.Ordinal)
            && !record.EarliestAvailableAtUtc.HasValue)
        {
            throw new InvalidOperationException(
                "FROM_RETENTION_BOUNDARY capture must declare the earliest available source timestamp.");
        }

        if (!IsSha256(record.RawPayloadSha256)
            || !IsSha256(record.SchemaFingerprintSha256)
            || !IsSha256(record.PermittedFieldSetSha256))
        {
            throw new InvalidOperationException("Whole-source capture hashes must be 64-character SHA-256 hex values.");
        }

        var actualPayloadSha = Sha256(record.RawPayloadJson);
        if (!string.Equals(actualPayloadSha, record.RawPayloadSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Whole-source connector raw payload hash does not match the retained payload.");
        }

        try
        {
            if (JsonNode.Parse(record.SourcePositionJson) is not JsonObject)
            {
                throw new InvalidOperationException(
                    "Whole-source connector source position must be a JSON object.");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Whole-source connector source position is not valid JSON.", exception);
        }
    }

    private static void ValidateCapturePermission(string connectorType, JsonObject configuration)
    {
        if (!(configuration["captureFullPermittedPayload"]?.GetValue<bool?>() ?? false))
        {
            throw new InvalidOperationException(
                $"Whole-source connector '{connectorType}' requires captureFullPermittedPayload=true. Normal Scout read access is not permission to retain the full continuity projection.");
        }

        if (string.Equals(connectorType, "restApi", StringComparison.OrdinalIgnoreCase))
        {
            var explicitlyRetainEntireObject = configuration["retainEntireResponseObject"] is JsonValue value
                && value.TryGetValue<bool>(out var retain)
                && retain;
            if (!explicitlyRetainEntireObject)
            {
                throw new InvalidOperationException(
                    "Generic REST whole-source capture requires retainEntireResponseObject=true. This is an explicit customer-permitted retention decision; selector/API access alone is not permission to journal every returned field.");
            }
        }
    }

    private static bool IsGenericSnapshotConnector(string connectorType)
        => string.Equals(connectorType, "sqlDatabase", StringComparison.OrdinalIgnoreCase)
            || string.Equals(connectorType, "restApi", StringComparison.OrdinalIgnoreCase)
            || string.Equals(connectorType, "csvUpload", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownHistory(string value)
        => string.Equals(value, LocalDataPlaneContracts.HistoryComplete, StringComparison.Ordinal)
            || string.Equals(value, LocalDataPlaneContracts.HistoryFromRetentionBoundary, StringComparison.Ordinal)
            || string.Equals(value, LocalDataPlaneContracts.HistoryOnDemand, StringComparison.Ordinal)
            || string.Equals(value, LocalDataPlaneContracts.HistorySnapshotOnly, StringComparison.Ordinal)
            || string.Equals(value, LocalDataPlaneContracts.HistoryUnknown, StringComparison.Ordinal);

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(static character =>
            (character >= '0' && character <= '9')
            || (character >= 'a' && character <= 'f')
            || (character >= 'A' && character <= 'F'));

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static JsonObject ParseConfiguration(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Connector configuration JSON is invalid.", exception);
        }
    }

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static string BuildSourceNamespace(Guid connectorInstallationId)
        => $"kyntic-connector:{connectorInstallationId:D}";

    private static DateTime? Min(DateTime? left, DateTime? right)
        => left is null ? right : right is null ? left : left <= right ? left : right;

    private static DateTime? Max(DateTime? left, DateTime? right)
        => left is null ? right : right is null ? left : left >= right ? left : right;
}

internal sealed record FullSourceCaptureRunResult(
    Guid ConnectorInstallationId,
    string ConnectorType,
    bool Executed,
    int PersistedRecords,
    bool CompletedGeneration,
    string? Reason);
