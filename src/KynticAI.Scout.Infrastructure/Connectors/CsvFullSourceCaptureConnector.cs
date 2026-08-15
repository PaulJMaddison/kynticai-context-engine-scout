using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;

namespace KynticAI.Scout.Infrastructure.Connectors;

/// <summary>
/// Whole-source capture for Scout's parsed CSV rows. A CSV upload is a complete snapshot of
/// the supplied file, not source-native change history, so the adapter reports SNAPSHOT_ONLY.
/// Fortress can rebuild current governed state from it, but pre-snapshot history is not invented.
/// </summary>
internal sealed class CsvFullSourceCaptureConnector : IUpgradeSourceCaptureConnector
{
    public string ConnectorType => "csvUpload";

    public Task<ConnectorSourceCaptureBatch> CaptureBatchAsync(
        ConnectorSourceCaptureRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = request.Configuration["rows"] as JsonArray
            ?? throw new InvalidOperationException("CSV full capture requires parsed rows.");
        var recordIdColumn = request.Configuration["sourceRecordIdColumn"]?.GetValue<string>()
            ?? request.Configuration["externalUserIdColumn"]?.GetValue<string>()
            ?? "externalUserId";
        var observedAtColumn = request.Configuration["observedAtColumn"]?.GetValue<string>() ?? "observedAtUtc";
        var sourceObjectType = request.Configuration["sourceObjectType"]?.GetValue<string>() ?? "csv_row";
        var offset = int.TryParse(request.ContinuationToken, out var parsed) ? Math.Max(0, parsed) : 0;
        var end = Math.Min(rows.Count, offset + request.MaxRecords);
        var records = new List<ConnectorSourceCaptureRecord>(Math.Max(0, end - offset));
        var snapshotHash = Sha256(rows.ToJsonString());

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
        return Task.FromResult(new ConnectorSourceCaptureBatch(
            records,
            complete ? null : end.ToString(),
            complete,
            JsonSerializer.Serialize(new
            {
                kind = "csv-snapshot",
                snapshotSha256 = snapshotHash,
                nextRow = end,
                completed = complete
            }),
            JsonSerializer.Serialize(new { offset, returned = records.Count, total = rows.Count })));
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
}
