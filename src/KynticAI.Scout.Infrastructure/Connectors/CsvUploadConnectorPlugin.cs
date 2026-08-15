using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Enums;

namespace KynticAI.Scout.Infrastructure.Connectors;

internal sealed class CsvUploadConnectorPlugin : ConnectorPluginBase
{
    public override string ConnectorType => "csvUpload";

    public override string DisplayName => "CSV Upload Connector";

    public override string Description => "Accepts safe, local parsed CSV rows. Optional explicit continuity settings retain the complete supplied snapshot for later Fortress reconstruction without turning the file into fake change history.";

    public override IReadOnlyList<string> Aliases => ["csv", "spreadsheetUpload"];

    public override IReadOnlyList<DataSourceKind> SupportedDataSourceKinds =>
        [DataSourceKind.Crm, DataSourceKind.SqlMetric, DataSourceKind.ProductUsage, DataSourceKind.EventStream];

    public override JsonObject GetConfigurationSchema()
        => new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("rows"),
            ["properties"] = new JsonObject
            {
                ["externalUserIdColumn"] = new JsonObject { ["type"] = "string", ["default"] = "externalUserId" },
                ["observedAtColumn"] = new JsonObject { ["type"] = "string", ["default"] = "observedAtUtc" },
                ["delimiter"] = new JsonObject { ["type"] = "string", ["default"] = "," },
                ["rows"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "Demo-safe parsed rows. Production file storage belongs behind a separate storage abstraction."
                },
                ["captureFullPermittedPayload"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Explicit permission to retain every supplied row/field in the customer-local continuity journal."
                },
                ["sourceRecordIdColumn"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Stable unique row key for whole-source replay."
                },
                ["sourceRecordIdIsUnique"] = new JsonObject { ["type"] = "boolean" },
                ["sourceObjectType"] = new JsonObject { ["type"] = "string" },
                ["redactionPolicyVersion"] = new JsonObject { ["type"] = "string" }
            }
        };

    public override JsonObject GetSampleConfiguration()
        => new()
        {
            ["externalUserIdColumn"] = "externalUserId",
            ["observedAtColumn"] = "observedAtUtc",
            ["delimiter"] = ",",
            ["captureFullPermittedPayload"] = true,
            ["sourceRecordIdColumn"] = "externalUserId",
            ["sourceRecordIdIsUnique"] = true,
            ["sourceObjectType"] = "customer_snapshot_row",
            ["redactionPolicyVersion"] = "customer-permitted.v1",
            ["rows"] = new JsonArray
            {
                new JsonObject
                {
                    ["externalUserId"] = "123",
                    ["observedAtUtc"] = "2026-05-11T10:30:00Z",
                    ["accountHealth"] = "green",
                    ["activeDays30"] = 26
                }
            }
        };

    public override async Task<ConnectorConfigurationValidationResult> ValidateConfigurationAsync(
        ConnectorConfigurationValidationRequest request,
        CancellationToken cancellationToken)
    {
        var baseline = await base.ValidateConfigurationAsync(request, cancellationToken);
        var errors = baseline.Errors.ToList();
        if (request.Configuration["rows"] is not JsonArray rows || rows.Count == 0)
        {
            errors.Add("CSV upload connector requires a non-empty rows array of parsed demo records.");
        }
        else
        {
            var externalUserIdColumn = request.Configuration["externalUserIdColumn"]?.GetValue<string>() ?? "externalUserId";
            for (var index = 0; index < rows.Count; index++)
            {
                if (rows[index] is not JsonObject row)
                {
                    errors.Add($"CSV upload connector rows[{index}] must be an object.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(row[externalUserIdColumn]?.GetValue<string>()))
                {
                    errors.Add($"CSV upload connector rows[{index}] requires '{externalUserIdColumn}'.");
                }
            }

            if (request.Configuration["captureFullPermittedPayload"]?.GetValue<bool?>() ?? false)
            {
                var recordIdColumn = request.Configuration["sourceRecordIdColumn"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(recordIdColumn))
                    errors.Add("CSV whole-source continuity requires sourceRecordIdColumn.");
                if (!(request.Configuration["sourceRecordIdIsUnique"]?.GetValue<bool?>() ?? false))
                    errors.Add("CSV whole-source continuity requires sourceRecordIdIsUnique=true.");
                if (!string.IsNullOrWhiteSpace(recordIdColumn))
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (var index = 0; index < rows.Count; index++)
                    {
                        if (rows[index] is not JsonObject row)
                            continue;
                        var recordId = row[recordIdColumn]?.ToString();
                        if (string.IsNullOrWhiteSpace(recordId))
                        {
                            errors.Add($"CSV whole-source continuity rows[{index}] requires '{recordIdColumn}'.");
                            continue;
                        }
                        if (!seen.Add(recordId))
                            errors.Add($"CSV whole-source continuity source record id '{recordId}' is duplicated.");
                    }
                }
            }
        }

        return baseline with
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    public override Task<ConnectorFetchResult> FetchAsync(
        ConnectorFetchRequest request,
        CancellationToken cancellationToken)
    {
        var externalUserIdColumn = request.Configuration["externalUserIdColumn"]?.GetValue<string>() ?? "externalUserId";
        var observedAtColumn = request.Configuration["observedAtColumn"]?.GetValue<string>() ?? "observedAtUtc";
        var rows = request.Configuration["rows"] as JsonArray
            ?? throw new InvalidOperationException("CSV upload connector requires rows.");
        var row = rows
            .Select(static item => item as JsonObject)
            .FirstOrDefault(item => item?[externalUserIdColumn]?.GetValue<string>() == request.Subject.ExternalUserId)
            ?? throw new InvalidOperationException($"No CSV row exists for user '{request.Subject.ExternalUserId}'.");

        var payload = (JsonObject)row.DeepClone();
        payload.Remove(externalUserIdColumn);
        var observedAtUtc = DateTime.UtcNow;
        if (payload[observedAtColumn] is JsonValue observedAtValue)
        {
            if (observedAtValue.TryGetValue<DateTime>(out var observedAt))
            {
                observedAtUtc = observedAt;
            }
            else if (observedAtValue.TryGetValue<string>(out var observedAtText) && DateTime.TryParse(observedAtText, out var parsed))
            {
                observedAtUtc = parsed;
            }

            payload.Remove(observedAtColumn);
        }

        return Task.FromResult(new ConnectorFetchResult(
            payload.ToJsonString(),
            payload,
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    source = ConnectorType,
                    request.Subject.ExternalUserId,
                    observedAtUtc
                }
            }),
            observedAtUtc,
            null,
            "{}"));
    }
}
