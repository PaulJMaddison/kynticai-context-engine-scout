using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KynticAI.Scout.Infrastructure.Connectors;

/// <summary>
/// Persists the source payload Scout actually observed before the selector output becomes a
/// derived ContextFact. The journal is customer-local and deliberately stores no connector
/// secret values. Subject fetches are marked SUBJECT_ON_DEMAND: they are useful continuity
/// evidence but are never allowed to masquerade as a whole-source capture.
/// </summary>
internal sealed class LocalSourceCaptureJournal(
    ScoutDbContext dbContext,
    IClock clock,
    ILogger<LocalSourceCaptureJournal> logger)
{
    private static readonly HashSet<string> CaptureConnectorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sqlDatabase",
        "restApi",
        "csvUpload"
    };

    public async Task CaptureSelectorOutcomeAsync(
        SelectorRuntimeContext runtimeContext,
        UserProfile user,
        SelectorExecutionMode mode,
        SelectorPipelineOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (!outcome.IsSuccess
            || mode is SelectorExecutionMode.Preview or SelectorExecutionMode.DryRun
            || string.IsNullOrWhiteSpace(outcome.RawSourceDataJson))
        {
            return;
        }

        var configuration = ParseObject(runtimeContext.DataSource.ConnectionConfigJson);
        var connectorType = configuration["connectorType"]?.GetValue<string>() ?? "mock";
        if (!CaptureConnectorTypes.Contains(connectorType))
        {
            return;
        }

        var installation = await dbContext.ConnectorInstallations
            .AsNoTracking()
            .Where(x => x.TenantId == runtimeContext.DataSource.TenantId
                && x.DataSourceId == runtimeContext.DataSource.Id
                && x.ConnectorType == connectorType)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (installation is null)
        {
            logger.LogWarning(
                "Skipping upgrade source capture for data source {DataSourceId}: no connector installation exists for {ConnectorType}.",
                runtimeContext.DataSource.Id,
                connectorType);
            return;
        }

        var rawPayload = outcome.RawSourceDataJson.Trim();
        var rawHash = Sha256(rawPayload);
        var normalizedNode = TryParseNode(outcome.NormalizedSourceDataJson) ?? TryParseNode(rawPayload);
        var schemaFingerprint = Sha256(BuildSchemaShape(normalizedNode));
        var recordId = ResolveRecordId(configuration, normalizedNode, user.ExternalUserId);
        var sourceObjectType = configuration["sourceObjectType"]?.GetValue<string>()
            ?? runtimeContext.DataSource.Kind.ToString();
        var observedAtUtc = outcome.CandidateFact?.ObservedAtUtc ?? clock.UtcNow;
        observedAtUtc = EnsureUtc(observedAtUtc);
        var sourceRecordedAtUtc = ResolveOptionalDate(configuration, normalizedNode, "sourceRecordedAtPath")
            ?? observedAtUtc;
        var redactionPolicyVersion = configuration["redactionPolicyVersion"]?.GetValue<string>()
            ?? "customer-permitted.v1";
        var fullPermittedPayload = configuration["captureFullPermittedPayload"]?.GetValue<bool?>() ?? true;
        var permittedFieldSetSha = Sha256(BuildPermittedFieldSet(normalizedNode));
        var sourcePosition = JsonSerializer.Serialize(new
        {
            kind = "subject-snapshot",
            observedAtUtc,
            rawPayloadSha256 = rawHash
        });
        var idempotencyKey = Sha256(string.Join('|',
            runtimeContext.DataSource.TenantId.ToString("D"),
            installation.Id.ToString("D"),
            sourceObjectType,
            recordId,
            sourcePosition,
            rawHash));

        var capture = new LocalSourceCaptureMetadataV1(
            LocalDataPlaneContracts.CaptureMetadataV1,
            installation.Id,
            connectorType,
            $"{connectorType}.subject-fetch.v1",
            LocalDataPlaneContracts.CaptureProfileFullPermittedV1,
            "1",
            configuration["sourceNamespace"]?.GetValue<string>() ?? connectorType,
            sourceObjectType,
            recordId,
            "snapshot",
            sourcePosition,
            observedAtUtc,
            sourceRecordedAtUtc,
            clock.UtcNow,
            schemaFingerprint,
            redactionPolicyVersion,
            fullPermittedPayload,
            idempotencyKey,
            LocalDataPlaneContracts.CoverageSubjectOnDemand,
            LocalDataPlaneContracts.HistoryOnDemand,
            null,
            rawHash,
            permittedFieldSetSha);

        var eventId = $"capture:{idempotencyKey}";
        var alreadyCaptured = await dbContext.SourceSystemEvents
            .AsNoTracking()
            .AnyAsync(x => x.TenantId == runtimeContext.DataSource.TenantId
                && x.SourceSystem == connectorType
                && x.EventId == eventId,
                cancellationToken);
        if (alreadyCaptured)
        {
            return;
        }

        var headersJson = JsonSerializer.Serialize(new
        {
            kynticCapture = capture,
            origin = "selector-fetch",
            selectorDefinitionId = runtimeContext.Selector.Id,
            dataSourceId = runtimeContext.DataSource.Id
        });

        var sourceEvent = SourceSystemEvent.Create(
            runtimeContext.DataSource.TenantId,
            installation.WorkspaceId,
            eventId,
            connectorType,
            $"capture.{sourceObjectType}.snapshot",
            user.ExternalUserId,
            null,
            user.Id,
            runtimeContext.DataSource.Id,
            rawPayload,
            headersJson,
            idempotencyKey[..32],
            observedAtUtc,
            clock.UtcNow);
        sourceEvent.MarkProcessed(
            1,
            "Customer-local source payload retained before selector-derived context output.",
            clock.UtcNow);
        dbContext.SourceSystemEvents.Add(sourceEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static JsonObject ParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static JsonNode? TryParseNode(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ResolveRecordId(JsonObject configuration, JsonNode? payload, string fallback)
    {
        var path = configuration["sourceRecordIdPath"]?.GetValue<string>()
            ?? configuration["sourceRecordIdColumn"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(path)
            && TryResolvePath(payload, path) is JsonValue value
            && value.TryGetValue<string>(out var stringValue)
            && !string.IsNullOrWhiteSpace(stringValue))
        {
            return stringValue.Trim();
        }

        return fallback.Trim();
    }

    private static DateTime? ResolveOptionalDate(JsonObject configuration, JsonNode? payload, string configurationKey)
    {
        var path = configuration[configurationKey]?.GetValue<string>();
        var node = TryResolvePath(payload, path);
        if (node is not JsonValue value)
        {
            return null;
        }
        if (value.TryGetValue<DateTime>(out var dateTime))
        {
            return EnsureUtc(dateTime);
        }
        if (value.TryGetValue<string>(out var text)
            && DateTimeOffset.TryParse(text, out var parsed))
        {
            return parsed.UtcDateTime;
        }
        return null;
    }

    private static JsonNode? TryResolvePath(JsonNode? root, string? path)
    {
        if (root is null || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        JsonNode? current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is JsonObject obj && obj.TryGetPropertyValue(segment, out var child))
            {
                current = child;
            }
            else
            {
                return null;
            }
        }
        return current;
    }

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static string BuildSchemaShape(JsonNode? node)
    {
        var builder = new StringBuilder();
        AppendSchema(node, builder);
        return builder.ToString();
    }

    private static void AppendSchema(JsonNode? node, StringBuilder builder)
    {
        switch (node)
        {
            case JsonObject obj:
                builder.Append('{');
                foreach (var item in obj.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    builder.Append(item.Key).Append(':');
                    AppendSchema(item.Value, builder);
                    builder.Append(';');
                }
                builder.Append('}');
                break;
            case JsonArray array:
                builder.Append("array[");
                if (array.Count > 0)
                {
                    AppendSchema(array[0], builder);
                }
                builder.Append(']');
                break;
            case JsonValue value when value.TryGetValue<bool>(out _):
                builder.Append("bool");
                break;
            case JsonValue value when value.TryGetValue<decimal>(out _):
                builder.Append("number");
                break;
            case JsonValue:
                builder.Append("string");
                break;
            case null:
                builder.Append("null");
                break;
            default:
                builder.Append("unknown");
                break;
        }
    }

    private static string BuildPermittedFieldSet(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return "non-object";
        }
        return string.Join('|', obj.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal));
    }

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
