using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KynticAI.Scout.Infrastructure.Connectors;

internal sealed class SqlConnectorPlugin(
    ScoutDbContext scoutDbContext,
    IOptionalCustomerOpsDatabase? customerOpsDatabase = null) : ConnectorPluginBase
{
    public override string ConnectorType => "sqlDatabase";

    public override string DisplayName => "SQL Database Connector";

    public override string Description => "Fetches subject rows from the current context database, customer operations database, or an external PostgreSQL connection. Optional explicit whole-source capture settings preserve a broader customer-permitted projection for seamless tier continuity.";

    public override IReadOnlyList<string> Aliases => ["sqlTable", "postgresql"];

    public override IReadOnlyList<DataSourceKind> SupportedDataSourceKinds => [DataSourceKind.SqlMetric, DataSourceKind.Crm, DataSourceKind.ProductUsage];

    public override JsonObject GetConfigurationSchema()
        => new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("tableName", "userIdColumn", "columns"),
            ["properties"] = new JsonObject
            {
                ["mode"] = new JsonObject { ["type"] = "string" },
                ["tableName"] = new JsonObject { ["type"] = "string" },
                ["userIdColumn"] = new JsonObject { ["type"] = "string" },
                ["tenantSlugColumn"] = new JsonObject { ["type"] = "string" },
                ["tenantSlug"] = new JsonObject { ["type"] = "string" },
                ["observedAtColumn"] = new JsonObject { ["type"] = "string" },
                ["columns"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "Narrow Scout subject/selector projection. This is not reused as the upgrade continuity projection."
                },
                ["captureFullPermittedPayload"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Explicit customer permission to retain the whole-source customer-permitted projection locally for tier continuity."
                },
                ["captureColumns"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "Explicit customer-approved whole-source projection. Required when captureFullPermittedPayload=true."
                },
                ["sourceRecordIdColumn"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Stable source record key used by whole-source capture and Fortress identity continuity."
                },
                ["sourceRecordIdIsUnique"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Must be explicitly true for portable keyset whole-source pagination."
                },
                ["sourceObjectType"] = new JsonObject { ["type"] = "string" },
                ["redactionPolicyVersion"] = new JsonObject { ["type"] = "string" },
                ["connectionString"] = new JsonObject { ["type"] = "string" },
                ["credentials"] = new JsonObject { ["type"] = "object" }
            }
        };

    public override JsonObject GetCredentialSchema()
        => new()
        {
            ["type"] = "object",
            ["description"] = "Optional external PostgreSQL connection string. Local demo modes use configured application databases instead.",
            ["properties"] = new JsonObject
            {
                ["connectionString"] = new JsonObject { ["type"] = "string", ["secret"] = true }
            }
        };

    public override JsonObject GetSampleConfiguration()
        => new()
        {
            ["mode"] = "customerOpsDatabase",
            ["tableName"] = "customer_context_rollups",
            ["tenantSlug"] = "demo",
            ["tenantSlugColumn"] = "tenant_slug",
            ["userIdColumn"] = "external_user_id",
            ["observedAtColumn"] = "observed_at_utc",
            ["columns"] = new JsonArray("plan_interest_signal", "active_days_30"),
            ["captureFullPermittedPayload"] = true,
            ["captureColumns"] = new JsonArray(
                "external_user_id",
                "tenant_slug",
                "observed_at_utc",
                "plan_interest_signal",
                "active_days_30"),
            ["sourceRecordIdColumn"] = "external_user_id",
            ["sourceRecordIdIsUnique"] = true,
            ["sourceObjectType"] = "customer_context_rollup",
            ["redactionPolicyVersion"] = "customer-permitted.v1"
        };

    public override async Task<ConnectorConfigurationValidationResult> ValidateConfigurationAsync(
        ConnectorConfigurationValidationRequest request,
        CancellationToken cancellationToken)
    {
        var baseline = await base.ValidateConfigurationAsync(request, cancellationToken);
        var errors = baseline.Errors.ToList();
        foreach (var field in new[] { "tableName", "userIdColumn" })
        {
            var identifier = request.Configuration[field]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(identifier) || !identifier.All(static c => char.IsLetterOrDigit(c) || c == '_'))
            {
                errors.Add($"SQL connector field '{field}' is required and must contain only letters, numbers, or underscores.");
            }
        }

        if (request.Configuration["columns"] is not JsonArray columns || columns.Count == 0)
        {
            errors.Add("SQL connector requires a non-empty columns array.");
        }
        else
        {
            ValidateIdentifierArray(columns, "SQL connector columns", errors);
        }

        var fullCaptureEnabled = request.Configuration["captureFullPermittedPayload"]?.GetValue<bool?>() ?? false;
        if (fullCaptureEnabled)
        {
            var sourceRecordId = request.Configuration["sourceRecordIdColumn"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(sourceRecordId) || !IsSafeIdentifier(sourceRecordId))
            {
                errors.Add("SQL whole-source continuity requires a safe sourceRecordIdColumn.");
            }
            if (!(request.Configuration["sourceRecordIdIsUnique"]?.GetValue<bool?>() ?? false))
            {
                errors.Add("SQL whole-source continuity requires sourceRecordIdIsUnique=true.");
            }
            if (request.Configuration["captureColumns"] is not JsonArray captureColumns || captureColumns.Count == 0)
            {
                errors.Add("SQL whole-source continuity requires a non-empty explicit captureColumns array; selector columns are not a full-capture fallback.");
            }
            else
            {
                ValidateIdentifierArray(captureColumns, "SQL captureColumns", errors);
            }
        }

        var mode = request.Configuration["mode"]?.GetValue<string>() ?? "customerOpsDatabase";
        if (!IsSupportedMode(mode))
        {
            errors.Add($"SQL connector mode '{mode}' is not supported.");
        }

        if (string.Equals(mode, "connectionString", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(request.Configuration["connectionString"]?.GetValue<string>())
            && string.IsNullOrWhiteSpace(request.Credentials["connectionString"]?.GetValue<string>()))
        {
            errors.Add("SQL connector connectionString mode requires a connectionString in configuration or credentials.");
        }

        return baseline with
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    public override async Task<ConnectorHealthCheckResult> CheckHealthAsync(
        ConnectorHealthCheckRequest request,
        CancellationToken cancellationToken)
    {
        var connection = await OpenConnectionAsync(request.Configuration, request.Credentials, cancellationToken);
        try
        {
            return new ConnectorHealthCheckResult(
                true,
                "healthy",
                [$"Successfully opened SQL connection for connector '{ConnectorType}'."],
                "{}",
                DateTime.UtcNow);
        }
        finally
        {
            if (connection != scoutDbContext.Database.GetDbConnection()
                && connection != customerOpsDatabase?.Connection)
            {
                await connection.DisposeAsync();
            }
        }
    }

    public override async Task<ConnectorFetchResult> FetchAsync(
        ConnectorFetchRequest request,
        CancellationToken cancellationToken)
    {
        var configuration = request.Configuration;
        var tableName = GetIdentifier(configuration, "tableName");
        var userIdColumn = GetIdentifier(configuration, "userIdColumn");
        var tenantSlugColumn = configuration["tenantSlugColumn"]?.GetValue<string>();
        var observedAtColumn = configuration["observedAtColumn"]?.GetValue<string>();
        var columns = configuration["columns"]?.AsArray()
            .Select(static node => node?.GetValue<string>() ?? string.Empty)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray() ?? throw new InvalidOperationException("SQL connector requires a columns array.");
        var connection = await OpenConnectionAsync(configuration, request.Credentials, cancellationToken);
        var disposeConnection = connection != scoutDbContext.Database.GetDbConnection()
            && connection != customerOpsDatabase?.Connection;

        try
        {
            await using var command = connection.CreateCommand();
            var selectColumns = columns.Select(QuoteIdentifier).ToList();
            if (!string.IsNullOrWhiteSpace(observedAtColumn) && columns.All(x => !string.Equals(x, observedAtColumn, StringComparison.OrdinalIgnoreCase)))
            {
                selectColumns.Add(QuoteIdentifier(observedAtColumn));
            }

            if (!string.IsNullOrWhiteSpace(tenantSlugColumn) && columns.All(x => !string.Equals(x, tenantSlugColumn, StringComparison.OrdinalIgnoreCase)))
            {
                selectColumns.Add(QuoteIdentifier(tenantSlugColumn));
            }

            var whereClauses = new List<string> { $"{QuoteIdentifier(userIdColumn)} = @userId" };
            if (!string.IsNullOrWhiteSpace(tenantSlugColumn))
            {
                whereClauses.Add($"{QuoteIdentifier(tenantSlugColumn)} = @tenantSlug");
            }

            var orderByClause = string.IsNullOrWhiteSpace(observedAtColumn)
                ? string.Empty
                : $" order by {QuoteIdentifier(observedAtColumn)} desc";
            command.CommandText = string.Create(
                CultureInfo.InvariantCulture,
                $"select {string.Join(", ", selectColumns)} from {QuoteIdentifier(tableName)} where {string.Join(" and ", whereClauses)}{orderByClause} limit 1");

            var userParameter = command.CreateParameter();
            userParameter.ParameterName = "@userId";
            userParameter.Value = request.Subject.ExternalUserId;
            command.Parameters.Add(userParameter);

            if (!string.IsNullOrWhiteSpace(tenantSlugColumn))
            {
                var tenantParameter = command.CreateParameter();
                tenantParameter.ParameterName = "@tenantSlug";
                tenantParameter.Value = configuration["tenantSlug"]?.GetValue<string>() ?? throw new InvalidOperationException("SQL connector requires tenantSlug when tenantSlugColumn is configured.");
                command.Parameters.Add(tenantParameter);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"No SQL row exists for user '{request.Subject.ExternalUserId}' in table '{tableName}'.");
            }

            var payload = new JsonObject();
            var observedAtUtc = DateTime.UtcNow;
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var name = reader.GetName(index);
                var value = reader.IsDBNull(index) ? null : reader.GetValue(index);
                if (string.Equals(name, observedAtColumn, StringComparison.OrdinalIgnoreCase))
                {
                    observedAtUtc = value switch
                    {
                        DateTime dateTime => NormalizeUtc(dateTime),
                        DateTimeOffset offset => offset.UtcDateTime,
                        string stringValue when DateTimeOffset.TryParse(stringValue, out var parsed) => parsed.UtcDateTime,
                        _ => observedAtUtc
                    };
                    continue;
                }

                payload[name] = JsonValue.Create(value);
            }

            return new ConnectorFetchResult(
                payload.ToJsonString(),
                payload,
                JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        source = ConnectorType,
                        tableName,
                        request.Subject.ExternalUserId,
                        observedAtUtc
                    }
                }),
                observedAtUtc,
                null,
                "{}");
        }
        finally
        {
            if (disposeConnection)
            {
                await connection.DisposeAsync();
            }
        }
    }

    private async Task<DbConnection> OpenConnectionAsync(JsonObject configuration, JsonObject credentials, CancellationToken cancellationToken)
    {
        var mode = configuration["mode"]?.GetValue<string>() ?? "customerOpsDatabase";
        return mode.Trim().ToLowerInvariant() switch
        {
            "currentdatabase" => await OpenSharedConnectionAsync(scoutDbContext.Database.GetDbConnection(), cancellationToken),
            "customeropsdatabase" => customerOpsDatabase?.Connection is null
                ? throw new InvalidOperationException("The 'customerOpsDatabase' SQL connector mode is only available when the optional CustomerOps reference data store is enabled.")
                : await OpenSharedConnectionAsync(customerOpsDatabase!.Connection!, cancellationToken),
            "connectionstring" => await OpenExternalConnectionAsync(configuration["connectionString"]?.GetValue<string>() ?? requestCredential(configuration, credentials), cancellationToken),
            _ => throw new InvalidOperationException($"SQL connector mode '{mode}' is not supported.")
        };
    }

    private static string? requestCredential(JsonObject configuration, JsonObject credentials)
        => credentials["connectionString"]?.GetValue<string>();

    private static async Task<DbConnection> OpenSharedConnectionAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return connection;
    }

    private static async Task<DbConnection> OpenExternalConnectionAsync(string? connectionString, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("SQL connector requires a connectionString in configuration or credentials.");
        }

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string GetIdentifier(JsonObject config, string key)
    {
        var value = config[key]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value) || !IsSafeIdentifier(value))
        {
            throw new InvalidOperationException($"SQL connector field '{key}' contains unsupported characters.");
        }

        return value;
    }

    private static void ValidateIdentifierArray(JsonArray values, string label, List<string> errors)
    {
        foreach (var value in values)
        {
            var columnName = value?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(columnName) || !IsSafeIdentifier(columnName))
                errors.Add($"{label} must contain only letters, numbers, or underscores.");
        }
    }

    private static bool IsSafeIdentifier(string value)
        => value.All(static c => char.IsLetterOrDigit(c) || c == '_');

    private static bool IsSupportedMode(string mode)
        => string.Equals(mode, "currentDatabase", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "customerOpsDatabase", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "connectionString", StringComparison.OrdinalIgnoreCase);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
