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
/// must provide an explicitly authorised collection endpoint and stable source-record ID path.
///
/// Generic list/cursor APIs are deliberately SNAPSHOT_ONLY. A cursor/token may be retained as
/// provenance, but this adapter never upgrades it into an exact temporal ordering contract. A
/// provider-specific connector is required for COMPLETE/FROM_RETENTION_BOUNDARY history.
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
        if (!(configuration["captureFullPermittedPayload"]?.GetValue<bool?>() ?? false))
        {
            throw new InvalidOperationException(
                "REST whole-source capture requires captureFullPermittedPayload=true. Ordinary API access is not permission to retain the entire collection for tier continuity.");
        }
        if (!(configuration["retainEntireResponseObject"]?.GetValue<bool?>() ?? false))
        {
            throw new InvalidOperationException(
                "REST whole-source capture requires retainEntireResponseObject=true after customer allow-list/redaction review.");
        }

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
        var sourceObjectType = configuration["sourceObjectType"]?.GetValue<string>()
            ?? throw new InvalidOperationException("REST full capture requires sourceObjectType.");

        var declaredHistory = configuration["captureHistoryCompleteness"]?.GetValue<string>()
            ?? LocalDataPlaneContracts.HistorySnapshotOnly;
        if (!string.Equals(declaredHistory, LocalDataPlaneContracts.HistorySnapshotOnly, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(declaredHistory, LocalDataPlaneContracts.HistoryUnknown, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Generic REST whole-source capture cannot claim exact historical coverage. Use a provider-specific ordered change-feed connector.");
        }

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
        var pageIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[index] as JsonObject
                ?? throw new InvalidOperationException($"REST full capture item {index} is not an object.");
            var recordId = ScalarText(ResolvePath(item, recordIdPath));
            if (string.IsNullOrWhiteSpace(recordId))
                throw new InvalidOperationException($"REST full capture item {index} has no stable source record ID at '{recordIdPath}'.");
            if (!pageIds.Add(recordId))
            {
                throw new InvalidOperationException(
                    $"REST full capture page contains duplicate source record id '{recordId}'. A stable record identity is required for continuity replay.");
            }

            var observedAt = ParseUtc(ResolvePath(item, observedAtPath)) ?? request.RequestedAtUtc;
            var nativePosition = ScalarText(ResolvePath(item, sourcePositionPath));
            var operation = string.IsNullOrWhiteSpace(nativePosition)
                ? "snapshot"
                : ScalarText(ResolvePath(item, operationPath));
            operation = string.IsNullOrWhiteSpace(operation) ? "snapshot" : operation.Trim().ToLowerInvariant();
            var itemJson = item.ToJsonString();
            var rawHash = Sha256(itemJson);
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

            // Even when the API exposes an opaque/native token, this generic adapter does not
            // know whether that token is globally monotonic or whether pagination represents one
            // stable source snapshot. Preserve it as provenance but keep temporal fidelity honest.
            var effectiveHistory = LocalDataPlaneContracts.HistorySnapshotOnly;
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
                null,
                idempotency));
        }

        var nextCursor = ScalarText(ResolvePath(root, nextCursorPath));
        if (string.IsNullOrWhiteSpace(nextCursor)
            && response.Headers.TryGetValues("X-Next-Cursor", out var headerValues))
        {
            nextCursor = headerValues.FirstOrDefault();
        }
        if (!string.IsNullOrWhiteSpace(nextCursor)
            && string.Equals(nextCursor, request.ContinuationToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "REST full-source pagination returned the same continuation cursor. Refusing an infinite/ambiguous capture generation.");
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
                responseEtag = response.Headers.ETag?.Tag,
                consistency = "api-defined-best-effort",
                pointInTimeSnapshot = false
            }),
            JsonSerializer.Serialize(new
            {
                statusCode = (int)response.StatusCode,
                returned = records.Count,
                hasNativePosition = !string.IsNullOrWhiteSpace(sourcePositionPath),
                history = LocalDataPlaneContracts.HistorySnapshotOnly,
                exactNativeOrderingClaimed = false
            }),
            LocalDataPlaneContracts.CurrentStateApiCursor,
            LocalDataPlaneContracts.HistorySnapshotOnly);
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

    private static string SchemaFingerprint(JsonObject payload)
        => Sha256(string.Join('|', payload.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}:{x.Value?.GetValueKind()}")));

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
