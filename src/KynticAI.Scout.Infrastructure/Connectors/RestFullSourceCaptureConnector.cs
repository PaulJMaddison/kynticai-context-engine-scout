using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;

namespace KynticAI.Scout.Infrastructure.Connectors;

/// <summary>
/// Whole-source companion for the generic REST connector. The customer/connector definition
/// must provide an explicit collection endpoint and source-record ID path. History is only
/// marked complete when the connector definition explicitly declares a source-native history
/// contract; ordinary paginated list APIs remain SNAPSHOT_ONLY.
/// </summary>
internal sealed class RestFullSourceCaptureConnector(IHttpClientFactory httpClientFactory)
    : IUpgradeSourceCaptureConnector
{
    public string ConnectorType => "restApi";

    public async Task<ConnectorSourceCaptureBatch> CaptureBatchAsync(
        ConnectorSourceCaptureRequest request,
        CancellationToken cancellationToken)
    {
        var configuration = request.Configuration;
        var baseUrl = configuration["baseUrl"]?.GetValue<string>()
            ?? throw new InvalidOperationException("REST full capture requires baseUrl.");
        var capturePath = configuration["capturePathTemplate"]?.GetValue<string>()
            ?? throw new InvalidOperationException("REST full capture requires capturePathTemplate for a collection endpoint.");
        var recordIdPath = configuration["sourceRecordIdPath"]?.GetValue<string>()
            ?? throw new InvalidOperationException("REST full capture requires sourceRecordIdPath.");
        var itemsPath = configuration["captureItemsPath"]?.GetValue<string>() ?? "items";
        var nextCursorPath = configuration["captureNextCursorPath"]?.GetValue<string>() ?? "nextCursor";
        var cursorParameter = configuration["captureCursorQueryParameter"]?.GetValue<string>() ?? "cursor";
        var limitParameter = configuration["captureLimitQueryParameter"]?.GetValue<string>() ?? "limit";
        var sourcePositionPath = configuration["captureSourcePositionPath"]?.GetValue<string>();
        var observedAtPath = configuration["captureObservedAtPath"]?.GetValue<string>()
            ?? configuration["observedAtPath"]?.GetValue<string>();
        var operationPath = configuration["captureOperationPath"]?.GetValue<string>();
        var sourceObjectType = configuration["sourceObjectType"]?.GetValue<string>() ?? "rest_record";
        var declaredHistory = configuration["captureHistoryCompleteness"]?.GetValue<string>()
            ?? LocalDataPlaneContracts.HistorySnapshotOnly;
        var earliestAvailable = ParseUtc(configuration["captureEarliestAvailableAtUtc"]);

        var uri = BuildUri(baseUrl, capturePath, cursorParameter, request.ContinuationToken, limitParameter, request.MaxRecords);
        var client = httpClientFactory.CreateClient("scout-connectors");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        ApplyHeaders(configuration, request.Credentials, httpRequest);
        using var response = await client.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        var root = JsonNode.Parse(raw)
            ?? throw new InvalidOperationException("REST full capture returned empty JSON.");
        var itemsNode = ResolvePath(root, itemsPath);
        var items = itemsNode switch
        {
            JsonArray array => array,
            null when root is JsonArray rootArray => rootArray,
            _ => throw new InvalidOperationException($"REST full capture path '{itemsPath}' did not resolve to an array.")
        };

        var records = new List<ConnectorSourceCaptureRecord>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[index] as JsonObject
                ?? throw new InvalidOperationException($"REST full capture item {index} is not an object.");
            var recordId = ScalarText(ResolvePath(item, recordIdPath));
            if (string.IsNullOrWhiteSpace(recordId))
                throw new InvalidOperationException($"REST full capture item {index} has no stable source record ID at '{recordIdPath}'.");

            var observedAt = ParseUtc(ResolvePath(item, observedAtPath)) ?? request.RequestedAtUtc;
            var operation = ScalarText(ResolvePath(item, operationPath));
            operation = string.IsNullOrWhiteSpace(operation) ? "snapshot" : operation.Trim().ToLowerInvariant();
            var itemJson = item.ToJsonString();
            var rawHash = Sha256(itemJson);
            var nativePosition = ScalarText(ResolvePath(item, sourcePositionPath));
            var position = string.IsNullOrWhiteSpace(nativePosition)
                ? JsonSerializer.Serialize(new
                {
                    kind = "rest-page-snapshot",
                    cursor = request.ContinuationToken,
                    index,
                    recordId,
                    observedAtUtc = observedAt,
                    payloadSha256 = rawHash
                })
                : JsonSerializer.Serialize(new
                {
                    kind = "rest-native-position",
                    value = nativePosition
                });
            var effectiveHistory = string.IsNullOrWhiteSpace(nativePosition)
                ? LocalDataPlaneContracts.HistorySnapshotOnly
                : NormalizeHistory(declaredHistory);
            var idempotency = Sha256($"{request.Installation.Id:D}|{sourceObjectType}|{recordId}|{operation}|{position}|{rawHash}");
            records.Add(new ConnectorSourceCaptureRecord(
                sourceObjectType,
                recordId.Trim(),
                operation,
                position,
                observedAt,
                observedAt,
                itemJson,
                (JsonObject)item.DeepClone(),
                SchemaFingerprint(item),
                rawHash,
                Sha256(string.Join('|', item.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal))),
                configuration["redactionPolicyVersion"]?.GetValue<string>() ?? "customer-permitted.v1",
                LocalDataPlaneContracts.CaptureProfileFullPermittedV1,
                "1",
                effectiveHistory,
                earliestAvailable,
                idempotency));
        }

        var nextCursor = ScalarText(ResolvePath(root, nextCursorPath));
        if (string.IsNullOrWhiteSpace(nextCursor)
            && response.Headers.TryGetValues("X-Next-Cursor", out var headerValues))
        {
            nextCursor = headerValues.FirstOrDefault();
        }
        var complete = string.IsNullOrWhiteSpace(nextCursor);
        return new ConnectorSourceCaptureBatch(
            records,
            complete ? null : nextCursor,
            complete,
            JsonSerializer.Serialize(new
            {
                kind = "rest-cursor",
                nextCursor,
                completed = complete,
                responseEtag = response.Headers.ETag?.Tag
            }),
            JsonSerializer.Serialize(new
            {
                statusCode = (int)response.StatusCode,
                returned = records.Count,
                hasNativePosition = !string.IsNullOrWhiteSpace(sourcePositionPath)
            }));
    }

    private static string BuildUri(
        string baseUrl,
        string path,
        string cursorParameter,
        string? cursor,
        string limitParameter,
        int limit)
    {
        var uri = baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(cursor))
            parts.Add($"{Uri.EscapeDataString(cursorParameter)}={Uri.EscapeDataString(cursor)}");
        parts.Add($"{Uri.EscapeDataString(limitParameter)}={limit}");
        var separator = uri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return uri + separator + string.Join('&', parts);
    }

    private static void ApplyHeaders(JsonObject configuration, JsonObject credentials, HttpRequestMessage request)
    {
        if (configuration["headers"] is JsonObject headers)
        {
            foreach (var item in headers)
            {
                var value = item.Value?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(value))
                    request.Headers.TryAddWithoutValidation(item.Key, value);
            }
        }
        if (credentials["bearerToken"]?.GetValue<string>() is { Length: > 0 } bearer)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (credentials["apiKey"]?.GetValue<string>() is { Length: > 0 } apiKey)
            request.Headers.TryAddWithoutValidation(configuration["apiKeyHeader"]?.GetValue<string>() ?? "X-API-Key", apiKey);
        if (credentials["basicUsername"]?.GetValue<string>() is { Length: > 0 } username
            && credentials["basicPassword"]?.GetValue<string>() is { Length: > 0 } password)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
        }
    }

    private static JsonNode? ResolvePath(JsonNode? root, string? path)
    {
        if (root is null || string.IsNullOrWhiteSpace(path))
            return null;
        JsonNode? current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is JsonObject obj && obj.TryGetPropertyValue(segment, out var child))
                current = child;
            else
                return null;
        }
        return current;
    }

    private static string? ScalarText(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        if (value.TryGetValue<string>(out var text))
            return text;
        return value.ToJsonString().Trim('"');
    }

    private static DateTime? ParseUtc(JsonNode? node)
    {
        var text = ScalarText(node);
        return DateTimeOffset.TryParse(text, out var parsed) ? parsed.UtcDateTime : null;
    }

    private static string NormalizeHistory(string value)
        => value.Trim().ToUpperInvariant() switch
        {
            LocalDataPlaneContracts.HistoryComplete => LocalDataPlaneContracts.HistoryComplete,
            LocalDataPlaneContracts.HistoryFromRetentionBoundary => LocalDataPlaneContracts.HistoryFromRetentionBoundary,
            _ => LocalDataPlaneContracts.HistorySnapshotOnly
        };

    private static string SchemaFingerprint(JsonObject payload)
        => Sha256(string.Join('|', payload.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}:{x.Value?.GetValueKind()}")));

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
