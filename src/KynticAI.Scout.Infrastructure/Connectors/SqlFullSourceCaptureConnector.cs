using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KynticAI.Scout.Infrastructure.Connectors;

/// <summary>
/// Whole-source companion for the Scout SQL connector. It captures every row in the
/// customer-permitted column set rather than only subjects selected by Scout context rules.
/// Generic SQL polling is snapshot semantics unless the customer points the connector at a
/// change/audit table; therefore this adapter deliberately reports SNAPSHOT_ONLY history and
/// never pretends to provide source-native CDC deletion history.
/// </summary>
internal sealed class SqlFullSourceCaptureConnector(
    ScoutDbContext scoutDbContext,
    CustomerOpsDbContext customerOpsDbContext) : IUpgradeSourceCaptureConnector
{
    public string ConnectorType => "sqlDatabase";

    public async Task<ConnectorSourceCaptureBatch> CaptureBatchAsync(
        ConnectorSourceCaptureRequest request,
        CancellationToken cancellationToken)
    {
        var configuration = request.Configuration;
        var tableName = Identifier(configuration, "tableName");
        var recordIdColumn = configuration["sourceRecordIdColumn"]?.GetValue<string>()
            ?? configuration["userIdColumn"]?.GetValue<string>()
            ?? throw new InvalidOperationException("SQL full capture requires sourceRecordIdColumn or userIdColumn.");
        if (!IsSafeIdentifier(recordIdColumn))
            throw new InvalidOperationException("SQL sourceRecordIdColumn contains unsupported characters.");

        var tenantSlugColumn = configuration["tenantSlugColumn"]?.GetValue<string>();
        var observedAtColumn = configuration["observedAtColumn"]?.GetValue<string>();
        var captureColumns = (configuration["captureColumns"] as JsonArray
            ?? configuration["columns"] as JsonArray)
            ?.Select(x => x?.GetValue<string>() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? throw new InvalidOperationException("SQL full capture requires captureColumns or columns.");
        if (!captureColumns.Contains(recordIdColumn, StringComparer.OrdinalIgnoreCase))
            captureColumns.Add(recordIdColumn);
        if (!string.IsNullOrWhiteSpace(observedAtColumn)
            && !captureColumns.Contains(observedAtColumn, StringComparer.OrdinalIgnoreCase))
            captureColumns.Add(observedAtColumn);
        foreach (var column in captureColumns)
            if (!IsSafeIdentifier(column))
                throw new InvalidOperationException($"SQL capture column '{column}' contains unsupported characters.");

        var offset = int.TryParse(request.ContinuationToken, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0, parsed)
            : 0;
        var connection = await OpenConnectionAsync(configuration, request.Credentials, cancellationToken);
        var dispose = connection != scoutDbContext.Database.GetDbConnection()
            && connection != customerOpsDbContext.Database.GetDbConnection();

        try
        {
            await using var command = connection.CreateCommand();
            var where = new List<string>();
            if (!string.IsNullOrWhiteSpace(tenantSlugColumn))
            {
                if (!IsSafeIdentifier(tenantSlugColumn))
                    throw new InvalidOperationException("SQL tenantSlugColumn contains unsupported characters.");
                where.Add($"{Quote(tenantSlugColumn)} = @tenantSlug");
                var tenantParameter = command.CreateParameter();
                tenantParameter.ParameterName = "@tenantSlug";
                tenantParameter.Value = configuration["tenantSlug"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("SQL full capture requires tenantSlug when tenantSlugColumn is configured.");
                command.Parameters.Add(tenantParameter);
            }

            var whereSql = where.Count == 0 ? string.Empty : $" where {string.Join(" and ", where)}";
            command.CommandText = $"select {string.Join(", ", captureColumns.Select(Quote))} from {Quote(tableName)}{whereSql} order by {Quote(recordIdColumn)} limit @limit offset @offset";
            var limitParameter = command.CreateParameter();
            limitParameter.ParameterName = "@limit";
            limitParameter.Value = request.MaxRecords;
            command.Parameters.Add(limitParameter);
            var offsetParameter = command.CreateParameter();
            offsetParameter.ParameterName = "@offset";
            offsetParameter.Value = offset;
            command.Parameters.Add(offsetParameter);

            var records = new List<ConnectorSourceCaptureRecord>(request.MaxRecords);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var ordinal = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                var payload = new JsonObject();
                string? recordId = null;
                DateTime? observedAt = null;
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    var name = reader.GetName(index);
                    var rawValue = reader.IsDBNull(index) ? null : reader.GetValue(index);
                    payload[name] = ToJsonValue(rawValue);
                    if (string.Equals(name, recordIdColumn, StringComparison.OrdinalIgnoreCase))
                        recordId = Convert.ToString(rawValue, CultureInfo.InvariantCulture);
                    if (string.Equals(name, observedAtColumn, StringComparison.OrdinalIgnoreCase))
                        observedAt = ToUtc(rawValue);
                }

                if (string.IsNullOrWhiteSpace(recordId))
                    throw new InvalidOperationException("SQL full capture encountered a row without a stable source record id.");

                var rawJson = payload.ToJsonString();
                var rawHash = Sha256(rawJson);
                var occurredAt = observedAt ?? request.RequestedAtUtc;
                var sourcePosition = JsonSerializer.Serialize(new
                {
                    kind = "sql-full-snapshot",
                    observedAtUtc = occurredAt,
                    recordId,
                    payloadSha256 = rawHash
                });
                var idempotency = Sha256($"{request.Installation.Id:D}|{tableName}|{recordId}|{sourcePosition}|{rawHash}");
                records.Add(new ConnectorSourceCaptureRecord(
                    configuration["sourceObjectType"]?.GetValue<string>() ?? tableName,
                    recordId,
                    "snapshot",
                    sourcePosition,
                    occurredAt,
                    observedAt,
                    rawJson,
                    (JsonObject)payload.DeepClone(),
                    SchemaFingerprint(payload),
                    rawHash,
                    Sha256(string.Join('|', captureColumns.OrderBy(x => x, StringComparer.Ordinal))),
                    configuration["redactionPolicyVersion"]?.GetValue<string>() ?? "customer-permitted.v1",
                    LocalDataPlaneContracts.CaptureProfileFullPermittedV1,
                    "1",
                    LocalDataPlaneContracts.HistorySnapshotOnly,
                    null,
                    idempotency));
                ordinal++;
            }

            var isComplete = records.Count < request.MaxRecords;
            var nextOffset = offset + records.Count;
            return new ConnectorSourceCaptureBatch(
                records,
                isComplete ? null : nextOffset.ToString(CultureInfo.InvariantCulture),
                isComplete,
                JsonSerializer.Serialize(new
                {
                    kind = "sql-snapshot-offset",
                    tableName,
                    nextOffset,
                    completed = isComplete
                }),
                JsonSerializer.Serialize(new { offset, returned = records.Count }));
        }
        finally
        {
            if (dispose)
                await connection.DisposeAsync();
        }
    }

    private async Task<DbConnection> OpenConnectionAsync(JsonObject configuration, JsonObject credentials, CancellationToken cancellationToken)
    {
        var mode = configuration["mode"]?.GetValue<string>() ?? "customerOpsDatabase";
        return mode.Trim().ToLowerInvariant() switch
        {
            "currentdatabase" => await OpenSharedAsync(scoutDbContext.Database.GetDbConnection(), cancellationToken),
            "customeropsdatabase" => await OpenSharedAsync(customerOpsDbContext.Database.GetDbConnection(), cancellationToken),
            "connectionstring" => await OpenExternalAsync(configuration["connectionString"]?.GetValue<string>() ?? credentials["connectionString"]?.GetValue<string>(), cancellationToken),
            _ => throw new InvalidOperationException($"SQL connector mode '{mode}' is not supported for full capture.")
        };
    }

    private static async Task<DbConnection> OpenSharedAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<DbConnection> OpenExternalAsync(string? connectionString, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("SQL full capture requires a local/customer-managed connection string reference.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static JsonNode? ToJsonValue(object? value)
        => value is null ? null : JsonSerializer.SerializeToNode(value);

    private static DateTime? ToUtc(object? value)
        => value switch
        {
            DateTime dateTime => dateTime.Kind == DateTimeKind.Utc ? dateTime : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            DateTimeOffset offset => offset.UtcDateTime,
            string text when DateTimeOffset.TryParse(text, out var parsed) => parsed.UtcDateTime,
            _ => null
        };

    private static string Identifier(JsonObject configuration, string key)
    {
        var value = configuration[key]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value) || !IsSafeIdentifier(value))
            throw new InvalidOperationException($"SQL connector field '{key}' is required and must be a safe identifier.");
        return value;
    }

    private static bool IsSafeIdentifier(string value)
        => value.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static string Quote(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string SchemaFingerprint(JsonObject payload)
        => Sha256(string.Join('|', payload.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}:{x.Value?.GetValueKind()}")));

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
