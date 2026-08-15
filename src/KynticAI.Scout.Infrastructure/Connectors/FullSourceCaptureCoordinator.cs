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
/// This is the path required for a genuinely lossless Scout -> Fortress upgrade. Selector reads
/// are useful evidence but do not prove estate-wide coverage.
///
/// Capture ownership is leased in the customer-local database. During a tier cutover the same
/// lease/checkpoint is the barrier that prevents Scout and Fortress from independently polling
/// the same source.
/// </summary>
internal sealed class FullSourceCaptureCoordinator(
    ScoutDbContext dbContext,
    IConnectorCredentialStore credentialStore,
    IEnumerable<IUpgradeSourceCaptureConnector> captureConnectors,
    IClock clock,
    ILogger<FullSourceCaptureCoordinator> logger)
{
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

        if (!checkpoint.TryAcquireLease(owner, TimeSpan.FromMinutes(5), now))
        {
            return new FullSourceCaptureRunResult(
                installation.Id, installation.ConnectorType, false, 0, false,
                "Capture lease is held by another local worker.");
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var configuration = ParseConfiguration(dataSource.ConnectionConfigJson);
            var resolved = await credentialStore.ResolveConfigurationSecretsAsync(
                dataSource.TenantId,
                configuration,
                cancellationToken);
            var credentials = resolved["credentials"] as JsonObject ?? new JsonObject();
            var batch = await connector.CaptureBatchAsync(
                new ConnectorSourceCaptureRequest(
                    installation,
                    dataSource,
                    resolved,
                    credentials,
                    checkpoint.ContinuationToken,
                    maxRecords,
                    now),
                cancellationToken);

            var persisted = 0;
            DateTime? earliest = null;
            DateTime? latest = null;
            foreach (var record in batch.Records)
            {
                ValidateRecord(record);
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
                var history = batch.Records.Count == 0
                    ? checkpoint.HistoryCompleteness
                    : batch.Records
                        .Select(x => x.HistoryCompleteness)
                        .Distinct(StringComparer.Ordinal)
                        .SingleOrDefault() ?? LocalDataPlaneContracts.HistoryUnknown;
                checkpoint.CompleteFullSourceGeneration(
                    owner,
                    batch.HighWaterMarkJson,
                    history,
                    clock.UtcNow);
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
        var duplicate = await dbContext.SourceSystemEvents
            .AsNoTracking()
            .AnyAsync(x => x.TenantId == installation.TenantId
                && x.SourceSystem == installation.ConnectorType
                && x.EventId == eventId,
                cancellationToken);
        if (duplicate)
        {
            return false;
        }

        var capture = new LocalSourceCaptureMetadataV1(
            LocalDataPlaneContracts.CaptureMetadataV1,
            installation.Id,
            installation.ConnectorType,
            $"{installation.ConnectorType}.full-source.v1",
            record.CaptureProfile,
            record.CaptureProfileVersion,
            installation.ConnectorType,
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
            record.PermittedFieldSetSha256);
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

    private static void ValidateRecord(ConnectorSourceCaptureRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.SourceObjectType)
            || string.IsNullOrWhiteSpace(record.SourceRecordId)
            || string.IsNullOrWhiteSpace(record.Operation)
            || string.IsNullOrWhiteSpace(record.SourcePositionJson)
            || string.IsNullOrWhiteSpace(record.RawPayloadJson)
            || string.IsNullOrWhiteSpace(record.IdempotencyKey)
            || string.IsNullOrWhiteSpace(record.RawPayloadSha256))
        {
            throw new InvalidOperationException("Whole-source connector returned an incomplete source record.");
        }
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
