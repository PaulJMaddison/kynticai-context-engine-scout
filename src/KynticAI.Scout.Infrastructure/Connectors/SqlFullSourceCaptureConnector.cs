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
/// Whole-source companion for the Scout SQL connector.
///
/// The ordinary Scout selector projection is intentionally NOT reused here. A customer may let
/// Scout use five columns while authorising the tier-neutral local data plane to retain twenty
/// columns for later Fortress capability. Whole-source capture therefore requires an explicit
/// customer-approved <c>captureColumns</c> set and <c>captureFullPermittedPayload=true</c>.
///
/// Pagination is keyset based on a stable unique source record ID. OFFSET is deliberately not
/// used: deletes/inserts before an offset can otherwise make a multi-page enumeration silently
/// skip or repeat rows. Keyset pagination still does not create a point-in-time database
/// snapshot; generic SQL therefore remains SNAPSHOT_ONLY and a live cutover may still require a
/// final write-freeze/recapture or a provider-specific CDC adapter.
/// </summary>
internal sealed class SqlFullSourceCaptureConnector(
    ScoutDbContext scoutDbContext,
    CustomerOpsDbContext customerOpsDbContext) : IUpgradeSourceCaptureConnector
{
    private const string CursorKind = "sql-keyset-v1";

    public string ConnectorType => "sqlDatabase";

    public async Task<ConnectorSourceCaptureBatch> CaptureBatchAsync(
        ConnectorSourceCaptureRequest request,
        CancellationToken cancellationToken)
    {
        var configuration = request.Configuration;
        RequireExplicitFullPermittedCapture(configuration);

        var tableName = Identifier(configuration, "tableName");
        var recordIdColumn = configuration["sourceRecordIdColumn"]?.GetValue<string>()
            ?? throw new InvalidOperationException(
                "SQL full capture requires an explicit sourceRecordIdColumn. The selector userIdColumn fallback is not sufficient for upgrade continuity.");
        if (!IsSafeIdentifier(recordIdColumn))
            throw new InvalidOperationException("SQL sourceRecordIdColumn contains unsupported characters.");

        var sourceRecordIdIsUnique = configuration["sourceRecordIdIsUnique"]?.GetValue<bool?>() ?? false;
        if (!sourceRecordIdIsUnique)
        {
            throw new InvalidOperationException(
                "SQL full capture requires sourceRecordIdIsUnique=true. Keyset continuity cannot be proved from a non-unique record key.");
        }

        var tenantSlugColumn = configuration["tenantSlugColumn"]?.GetValue<string>();
        var observedAtColumn = configuration["observedAtColumn"]?.GetValue<string>();
        var captureColumns = (configuration["captureColumns"] as JsonArray)
            ?.Select(x => x?.GetValue<string>() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? throw new InvalidOperationException(
                "SQL full capture requires an explicit captureColumns array. The ordinary Scout selector columns are intentionally not used as a fallback.");
        if (captureColumns.Count == 0)
            throw new InvalidOperationException("SQL captureColumns cannot be empty.");
        if (!captureColumns.Contains(recordIdColumn, StringComparer.OrdinalIgnoreCase))
            captureColumns.Add(recordIdColumn);
        if (!string.IsNullOrWhiteSpace(observedAtColumn)
            && !captureColumns.Contains(observedAtColumn, StringComparer.OrdinalIgnoreCase))
            captureColumns.Add(observedAtColumn);
        foreach (var column in captureColumns)
            if (!IsSafeIdentifier(column))
                throw new InvalidOperationException($"SQL capture column '{column}' contains unsupported characters.");

        var cursor = ParseCursor(request.ContinuationToken, tableName, recordIdColumn);
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

            if (cursor is not null)
            {
                where.Add($"{Quote(recordIdColumn)} > @afterRecordId");
                var afterParameter = command.CreateParameter();
                afterParameter.ParameterName = "@afterRecordId";
                afterParameter.Value = CursorValue(cursor);
                command.Parameters.Add(afterParameter);
            }

            var whereSql = where.Count == 0 ? string.Empty : $" where {string.Join(" and ", where)}";
            command.CommandText = $"select {string.Join(", ", captureColumns.Select(Quote))} from {Quote(tableName)}{whereSql} order by {Quote(recordIdColumn)} limit @limit";
            var limitParameter = command.CreateParameter();
            limitParameter.ParameterName = "@limit";
            limitParameter.Value = request.MaxRecords;
            command.Parameters.Add(limitParameter);

            var records = new List<ConnectorSourceCaptureRecord>(request.MaxRecords);
            object? lastRecordIdValue = null;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var payload = new JsonObject();
                string? recordId = null;
                DateTime? observedAt = null;
                object? rawRecordId = null;
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    var name = reader.GetName(index);
                    var rawValue = reader.IsDBNull(index) ? null : reader.GetValue(index);
                    payload[name] = ToJsonValue(rawValue);
                    if (string.Equals(name, recordIdColumn, StringComparison.OrdinalIgnoreCase))
                    {
                        rawRecordId = rawValue;
                        recordId = Convert.ToString(rawValue, CultureInfo.InvariantCulture);
                    }
                    if (string.Equals(name, observedAtColumn, StringComparison.OrdinalIgnoreCase))
                        observedAt = ToUtc(rawValue);
                }

                if (rawRecordId is null || string.IsNullOrWhiteSpace(recordId))
                    throw new InvalidOperationException("SQL full capture encountered a row without a stable source record id.");
                EnsureSupportedCursorValue(rawRecordId);
                lastRecordIdValue = rawRecordId;

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
            }

            var isComplete = records.Count < request.MaxRecords;
            var nextCursor = !isComplete && lastRecordIdValue is not null
                ? SerializeCursor(tableName, recordIdColumn, lastRecordIdValue)
                : null;
            return new ConnectorSourceCaptureBatch(
                records,
                nextCursor,
                isComplete,
                JsonSerializer.Serialize(new
                {
                    kind = CursorKind,
                    tableName,
                    recordIdColumn,
                    after = nextCursor is null ? null : Sha256(nextCursor),
                    completed = isComplete
                }),
                JsonSerializer.Serialize(new
                {
                    pagination = "keyset",
                    returned = records.Count,
                    pointInTimeSnapshot = false,
                    history = LocalDataPlaneContracts.HistorySnapshotOnly
                }),
                LocalDataPlaneContracts.CurrentStateLiveKeyset,
                LocalDataPlaneContracts.HistorySnapshotOnly);
        }
        finally
        {
            if (dispose)
                await connection.DisposeAsync();
        }
    }

    private static void RequireExplicitFullPermittedCapture(JsonObject configuration)
    {
        var allowed = configuration["captureFullPermittedPayload"]?.GetValue<bool?>() ?? false;
        if (!allowed)
        {
            throw new InvalidOperationException(
                "SQL whole-source capture requires captureFullPermittedPayload=true. Normal Scout query access is not permission to retain the full customer-permitted projection for tier continuity.");
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

    private static SqlKeysetCursor? ParseCursor(string? token, string tableName, string recordIdColumn)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        SqlKeysetCursor cursor;
        try
        {
            cursor = JsonSerializer.Deserialize<SqlKeysetCursor>(token)
                ?? throw new InvalidOperationException("SQL keyset continuation token is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("SQL full-source continuation token is invalid JSON.", exception);
        }
        if (!string.Equals(cursor.Kind, CursorKind, StringComparison.Ordinal)
            || !string.Equals(cursor.TableName, tableName, StringComparison.Ordinal)
            || !string.Equals(cursor.RecordIdColumn, recordIdColumn, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SQL full-source continuation token does not belong to this table/record-id contract.");
        }
        return cursor;
    }

    private static string SerializeCursor(string tableName, string recordIdColumn, object value)
    {
        var (type, text) = CursorText(value);
        return JsonSerializer.Serialize(new SqlKeysetCursor(
            CursorKind,
            tableName,
            recordIdColumn,
            type,
            text));
    }

    private static object CursorValue(SqlKeysetCursor cursor)
        => cursor.Type switch
        {
            "string" => cursor.Value,
            "guid" => Guid.ParseExact(cursor.Value, "D"),
            "int16" => short.Parse(cursor.Value, CultureInfo.InvariantCulture),
            "int32" => int.Parse(cursor.Value, CultureInfo.InvariantCulture),
            "int64" => long.Parse(cursor.Value, CultureInfo.InvariantCulture),
            "uint16" => ushort.Parse(cursor.Value, CultureInfo.InvariantCulture),
            "uint32" => uint.Parse(cursor.Value, CultureInfo.InvariantCulture),
            "uint64" => ulong.Parse(cursor.Value, CultureInfo.InvariantCulture),
            "decimal" => decimal.Parse(cursor.Value, NumberStyles.Number, CultureInfo.InvariantCulture),
            "double" => double.Parse(cursor.Value, NumberStyles.Float, CultureInfo.InvariantCulture),
            "single" => float.Parse(cursor.Value, NumberStyles.Float, CultureInfo.InvariantCulture),
            "datetime" => DateTime.Parse(cursor.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            "datetimeoffset" => DateTimeOffset.Parse(cursor.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            _ => throw new InvalidOperationException($"SQL keyset continuation type '{cursor.Type}' is not supported.")
        };

    private static void EnsureSupportedCursorValue(object value)
    {
        _ = CursorText(value);
    }

    private static (string Type, string Value) CursorText(object value)
        => value switch
        {
            string text => ("string", text),
            Guid guid => ("guid", guid.ToString("D")),
            short number => ("int16", number.ToString(CultureInfo.InvariantCulture)),
            int number => ("int32", number.ToString(CultureInfo.InvariantCulture)),
            long number => ("int64", number.ToString(CultureInfo.InvariantCulture)),
            ushort number => ("uint16", number.ToString(CultureInfo.InvariantCulture)),
            uint number => ("uint32", number.ToString(CultureInfo.InvariantCulture)),
            ulong number => ("uint64", number.ToString(CultureInfo.InvariantCulture)),
            decimal number => ("decimal", number.ToString(CultureInfo.InvariantCulture)),
            double number => ("double", number.ToString("R", CultureInfo.InvariantCulture)),
            float number => ("single", number.ToString("R", CultureInfo.InvariantCulture)),
            DateTime dateTime => ("datetime", dateTime.ToString("O", CultureInfo.InvariantCulture)),
            DateTimeOffset dateTimeOffset => ("datetimeoffset", dateTimeOffset.ToString("O", CultureInfo.InvariantCulture)),
            _ => throw new InvalidOperationException(
                $"SQL sourceRecordIdColumn CLR type '{value.GetType().FullName}' is not supported by the portable keyset cursor. Use a stable string/Guid/numeric/time key or a provider-specific capture adapter.")
        };

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

    private sealed record SqlKeysetCursor(
        string Kind,
        string TableName,
        string RecordIdColumn,
        string Type,
        string Value);
}
