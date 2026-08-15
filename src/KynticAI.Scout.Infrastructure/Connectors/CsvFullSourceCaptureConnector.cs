using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;

namespace KynticAI.Scout.Infrastructure.Connectors;

/// <summary>
/// Whole-source capture for Scout's parsed CSV rows. A CSV upload is a complete immutable-at-
/// capture snapshot of the supplied row set, not source-native change history, so the adapter
/// reports SNAPSHOT_ONLY. The continuation token includes the complete row-set hash; changing
/// rows between pages fails closed instead of combining two file versions into one generation.
/// </summary>
internal sealed class CsvFullSourceCaptureConnector : IUpgradeSourceCaptureConnector
{
    private const string CursorKind = "csv-snapshot-v1";

    public string ConnectorType => "csvUpload";

    public Task<ConnectorSourceCaptureBatch> CaptureBatchAsync(
        ConnectorSourceCaptureRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!(request.Configuration["captureFullPermittedPayload"]?.GetValue<bool?>() ?? false))
        {
            throw new InvalidOperationException(
                "CSV whole-source capture requires captureFullPermittedPayload=true. Upload/read access alone is not the continuity retention decision.");
        }

        var rows = request.Configuration["rows"] as JsonArray
            ?? throw new InvalidOperationException("CSV full capture requires parsed rows.");
        var recordIdColumn = request.Configuration["sourceRecordIdColumn"]?.GetValue<string>()
            ?? throw new InvalidOperationException(
                "CSV full capture requires an explicit sourceRecordIdColumn; the subject lookup column is not an implicit upgrade identity contract.");
        if (!(request.Configuration["sourceRecordIdIsUnique"]?.GetValue<bool?>() ?? false))
        {
            throw new InvalidOperationException(
                "CSV whole-source capture requires sourceRecordIdIsUnique=true.");
        }
        var observedAtColumn = request.Configuration["observedAtColumn"]?.GetValue<string>() ?? "observedAtUtc";
        var sourceObjectType = request.Configuration["sourceObjectType"]?.GetValue<string>() ?? "csv_row";
        var snapshotHash = Sha256(rows.ToJsonString());
        var offset = ParseCursor(request.ContinuationToken, snapshotHash);

        ValidateUniqueRecordIds(rows, recordIdColumn);

        var end = Math.Min(rows.Count, offset + request.MaxRecords);
        var records = new List<ConnectorSourceCaptureRecord>(Math.Max(0, end - offset));

        for (var index = offset; index < end; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[index] as JsonObject
                ?? throw new InvalidOperationException($"CSV rows[{index}] must be an object.");
            var recordId = row[recordIdColumn]?.ToString();
            if (string.IsNullOrWhiteSpace(recordId))
                throw new InvalidOperationException($"CSV rows[{index}] has no stable '{recordIdColumn}' value.");

            var observedAt = ResolveUtc(row[observedAtColumn]) ?? request.RequestedAtUtc;
            var rawJson = row.ToJsonString();
            var rawHash = Sha256(rawJson);
            var position = JsonSerializer.Serialize(new
            {
                kind = "csv-snapshot",
                snapshotSha256 = snapshotHash,
                rowIndex = index,
                recordId
            });
            var idempotency = Sha256($"{request.Installation.Id:D}|{snapshotHash}|{index}|{recordId}|{rawHash}");
            records.Add(new ConnectorSourceCaptureRecord(
                sourceObjectType,
                recordId.Trim(),
                "snapshot",
                position,
                observedAt,
                observedAt,
                rawJson,
                (JsonObject)row.DeepClone(),
                SchemaFingerprint(row),
                rawHash,
                Sha256(string.Join('|', row.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal))),
                request.Configuration["redactionPolicyVersion"]?.GetValue<string>() ?? "customer-permitted.v1",
                LocalDataPlaneContracts.CaptureProfileFullPermittedV1,
                "1",
                LocalDataPlaneContracts.HistorySnapshotOnly,
                null,
                idempotency));
        }

        var complete = end >= rows.Count;
        var nextCursor = complete
            ? null
            : JsonSerializer.Serialize(new CsvSnapshotCursor(CursorKind, snapshotHash, end));
        return Task.FromResult(new ConnectorSourceCaptureBatch(
            records,
            nextCursor,
            complete,
            JsonSerializer.Serialize(new
            {
                kind = "csv-snapshot",
                snapshotSha256 = snapshotHash,
                nextRow = end,
                completed = complete
            }),
            JsonSerializer.Serialize(new
            {
                snapshotSha256 = snapshotHash,
                offset,
                returned = records.Count,
                total = rows.Count,
                pointInTimeSnapshot = true,
                history = LocalDataPlaneContracts.HistorySnapshotOnly
            }),
            LocalDataPlaneContracts.CurrentStateImmutableSnapshot,
            LocalDataPlaneContracts.HistorySnapshotOnly));
    }

    private static int ParseCursor(string? token, string currentSnapshotHash)
    {
        if (string.IsNullOrWhiteSpace(token))
            return 0;
        CsvSnapshotCursor cursor;
        try
        {
            cursor = JsonSerializer.Deserialize<CsvSnapshotCursor>(token)
                ?? throw new InvalidOperationException("CSV snapshot continuation token is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("CSV snapshot continuation token is invalid JSON.", exception);
        }
        if (!string.Equals(cursor.Kind, CursorKind, StringComparison.Ordinal)
            || !string.Equals(cursor.SnapshotSha256, currentSnapshotHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "CSV row set changed during a paged FULL_SOURCE generation. Restart the generation from row zero; mixed snapshots are not upgrade evidence.");
        }
        if (cursor.NextRow < 0)
            throw new InvalidOperationException("CSV snapshot continuation token contains a negative row offset.");
        return cursor.NextRow;
    }

    private static void ValidateUniqueRecordIds(JsonArray rows, string recordIdColumn)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index] is not JsonObject row)
                throw new InvalidOperationException($"CSV rows[{index}] must be an object.");
            var recordId = row[recordIdColumn]?.ToString();
            if (string.IsNullOrWhiteSpace(recordId))
                throw new InvalidOperationException($"CSV rows[{index}] has no stable '{recordIdColumn}' value.");
            if (!seen.Add(recordId))
            {
                throw new InvalidOperationException(
                    $"CSV whole-source capture found duplicate source record id '{recordId}'. A unique stable record identity is required for seamless upgrade replay.");
            }
        }
    }

    private static DateTime? ResolveUtc(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        if (value.TryGetValue<DateTime>(out var dateTime))
            return dateTime.Kind == DateTimeKind.Utc ? dateTime : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        if (value.TryGetValue<string>(out var text) && DateTimeOffset.TryParse(text, out var parsed))
            return parsed.UtcDateTime;
        return null;
    }

    private static string SchemaFingerprint(JsonObject payload)
        => Sha256(string.Join('|', payload.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}:{x.Value?.GetValueKind()}")));

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record CsvSnapshotCursor(string Kind, string SnapshotSha256, int NextRow);
}
