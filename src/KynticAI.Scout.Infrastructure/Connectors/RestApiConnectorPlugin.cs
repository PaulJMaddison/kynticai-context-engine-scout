using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Enums;

namespace KynticAI.Scout.Infrastructure.Connectors;

internal sealed class RestApiConnectorPlugin(IHttpClientFactory httpClientFactory) : ConnectorPluginBase
{
    public override string ConnectorType => "restApi";

    public override string DisplayName => "REST API Connector";

    public override string Description => "Fetches subject payloads from generic HTTP APIs and can optionally enumerate an explicitly authorised collection for Scout-to-Fortress continuity. Generic REST collection paging remains snapshot/history-limited unless a provider-specific ordered change-feed connector proves stronger semantics.";

    public override IReadOnlyList<string> Aliases =>
    [
        "apiPayload",
        "crmApi",
        "billingApi",
        "telemetryApi",
        "productTelemetry",
        "supportApi"
    ];

    public override IReadOnlyList<DataSourceKind> SupportedDataSourceKinds =>
        [DataSourceKind.Crm, DataSourceKind.EventStream, DataSourceKind.ProductUsage, DataSourceKind.SqlMetric];

    public override JsonObject GetConfigurationSchema()
        => new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("baseUrl"),
            ["properties"] = new JsonObject
            {
                ["baseUrl"] = new JsonObject { ["type"] = "string" },
                ["pathTemplate"] = new JsonObject { ["type"] = "string" },
                ["method"] = new JsonObject { ["type"] = "string" },
                ["subjectQueryParameter"] = new JsonObject { ["type"] = "string" },
                ["subjectPathToken"] = new JsonObject { ["type"] = "string" },
                ["observedAtPath"] = new JsonObject { ["type"] = "string" },
                ["staticResponses"] = new JsonObject { ["type"] = "array" },
                ["responses"] = new JsonObject { ["type"] = "array" },
                ["headers"] = new JsonObject { ["type"] = "object" },
                ["apiKeyHeader"] = new JsonObject { ["type"] = "string" },
                ["credentials"] = new JsonObject { ["type"] = "object" },

                ["captureFullPermittedPayload"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Explicit customer decision to retain the full permitted collection locally for tier continuity."
                },
                ["retainEntireResponseObject"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Required with whole-source capture; confirms every returned object field is permitted after configured governance/redaction."
                },
                ["capturePathTemplate"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Collection endpoint used only for whole-source continuity enumeration."
                },
                ["sourceRecordIdPath"] = new JsonObject { ["type"] = "string" },
                ["sourceObjectType"] = new JsonObject { ["type"] = "string" },
                ["captureItemsPath"] = new JsonObject { ["type"] = "string", ["default"] = "items" },
                ["captureNextCursorPath"] = new JsonObject { ["type"] = "string", ["default"] = "nextCursor" },
                ["captureCursorQueryParameter"] = new JsonObject { ["type"] = "string", ["default"] = "cursor" },
                ["captureLimitQueryParameter"] = new JsonObject { ["type"] = "string", ["default"] = "limit" },
                ["captureObservedAtPath"] = new JsonObject { ["type"] = "string" },
                ["captureSourcePositionPath"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Optional source-native position value retained for provenance. Generic REST still cannot claim exact order without a provider-specific mapper."
                },
                ["captureOperationPath"] = new JsonObject { ["type"] = "string" },
                ["captureHistoryCompleteness"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Generic REST must remain SNAPSHOT_ONLY/UNKNOWN. Exact history belongs to a provider-specific connector."
                },
                ["captureEarliestAvailableAtUtc"] = new JsonObject { ["type"] = "string" },
                ["redactionPolicyVersion"] = new JsonObject { ["type"] = "string" }
            }
        };

    public override JsonObject GetCredentialSchema()
        => new()
        {
            ["type"] = "object",
            ["description"] = "Optional generic REST credentials. Values are persisted through the connector credential store and replaced with secret references.",
            ["properties"] = new JsonObject
            {
                ["bearerToken"] = new JsonObject { ["type"] = "string", ["secret"] = true },
                ["apiKey"] = new JsonObject { ["type"] = "string", ["secret"] = true },
                ["basicUsername"] = new JsonObject { ["type"] = "string" },
                ["basicPassword"] = new JsonObject { ["type"] = "string", ["secret"] = true }
            }
        };

    public override JsonObject GetSampleConfiguration()
        => new()
        {
            ["baseUrl"] = "https://api.example.com",
            ["pathTemplate"] = "/v1/customers/{externalUserId}",
            ["method"] = "GET",
            ["observedAtPath"] = "meta.observedAtUtc",
            ["captureFullPermittedPayload"] = true,
            ["retainEntireResponseObject"] = true,
            ["capturePathTemplate"] = "/v1/customers",
            ["sourceRecordIdPath"] = "id",
            ["sourceObjectType"] = "customer",
            ["captureItemsPath"] = "items",
            ["captureNextCursorPath"] = "nextCursor",
            ["captureCursorQueryParameter"] = "cursor",
            ["captureLimitQueryParameter"] = "limit",
            ["captureObservedAtPath"] = "updatedAtUtc",
            ["captureHistoryCompleteness"] = "SNAPSHOT_ONLY",
            ["redactionPolicyVersion"] = "customer-permitted.v1",
            ["credentials"] = new JsonObject
            {
                ["apiKey"] = "secret://tenant/data-source/apiKey"
            }
        };

    public override async Task<ConnectorConfigurationValidationResult> ValidateConfigurationAsync(
        ConnectorConfigurationValidationRequest request,
        CancellationToken cancellationToken)
    {
        var baseline = await base.ValidateConfigurationAsync(request, cancellationToken);
        var errors = baseline.Errors.ToList();

        if (string.IsNullOrWhiteSpace(request.Configuration["baseUrl"]?.GetValue<string>()))
        {
            errors.Add("REST connector requires baseUrl.");
        }
        else if (!IsHttpUri(request.Configuration["baseUrl"]!.GetValue<string>()))
        {
            errors.Add("REST connector baseUrl must be an absolute http or https URL.");
        }

        if (request.Configuration["staticResponses"] is null
            && request.Configuration["responses"] is null
            && string.IsNullOrWhiteSpace(request.Configuration["pathTemplate"]?.GetValue<string>())
            && string.IsNullOrWhiteSpace(request.Configuration["subjectQueryParameter"]?.GetValue<string>()))
        {
            errors.Add("REST connector requires pathTemplate, subjectQueryParameter, or staticResponses.");
        }

        var method = request.Configuration["method"]?.GetValue<string>() ?? "GET";
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("REST connector method must be GET or POST.");
        }

        var staticResponses = request.Configuration["staticResponses"] as JsonArray
            ?? request.Configuration["responses"] as JsonArray;
        if (staticResponses is not null)
        {
            ValidateStaticResponses(staticResponses, errors);
        }

        var fullCaptureEnabled = request.Configuration["captureFullPermittedPayload"]?.GetValue<bool?>() ?? false;
        if (fullCaptureEnabled)
        {
            if (!(request.Configuration["retainEntireResponseObject"]?.GetValue<bool?>() ?? false))
            {
                errors.Add("REST whole-source continuity requires retainEntireResponseObject=true as an explicit customer retention decision.");
            }
            if (string.IsNullOrWhiteSpace(request.Configuration["capturePathTemplate"]?.GetValue<string>()))
                errors.Add("REST whole-source continuity requires capturePathTemplate for a collection endpoint.");
            if (string.IsNullOrWhiteSpace(request.Configuration["sourceRecordIdPath"]?.GetValue<string>()))
                errors.Add("REST whole-source continuity requires sourceRecordIdPath.");
            if (string.IsNullOrWhiteSpace(request.Configuration["sourceObjectType"]?.GetValue<string>()))
                errors.Add("REST whole-source continuity requires sourceObjectType.");

            var declaredHistory = request.Configuration["captureHistoryCompleteness"]?.GetValue<string>() ?? "SNAPSHOT_ONLY";
            if (!string.Equals(declaredHistory, "SNAPSHOT_ONLY", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(declaredHistory, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Generic REST whole-source capture may only declare SNAPSHOT_ONLY or UNKNOWN history. Use a provider-specific ordered change-feed connector for COMPLETE/FROM_RETENTION_BOUNDARY claims.");
            }
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
        if (request.Configuration["staticResponses"] is JsonArray || request.Configuration["responses"] is JsonArray)
        {
            return new ConnectorHealthCheckResult(
                true,
                "healthy",
                ["REST connector is using staticResponses preview mode."],
                "{}",
                DateTime.UtcNow);
        }

        var baseUrl = request.Configuration["baseUrl"]?.GetValue<string>()
            ?? throw new InvalidOperationException("REST connector requires baseUrl.");
        var client = httpClientFactory.CreateClient("scout-connectors");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Head, baseUrl);
        ApplyHeaders(request.Configuration, request.Credentials, httpRequest);
        using var response = await client.SendAsync(httpRequest, cancellationToken);
        return new ConnectorHealthCheckResult(
            response.IsSuccessStatusCode,
            response.IsSuccessStatusCode ? "healthy" : "degraded",
            [$"HEAD {baseUrl} returned {(int)response.StatusCode}."],
            "{}",
            DateTime.UtcNow);
    }

    public override async Task<ConnectorFetchResult> FetchAsync(
        ConnectorFetchRequest request,
        CancellationToken cancellationToken)
    {
        var staticResponses = request.Configuration["staticResponses"] as JsonArray
            ?? request.Configuration["responses"] as JsonArray;
        if (staticResponses is not null)
        {
            var response = staticResponses
                .Select(static item => item as JsonObject)
                .FirstOrDefault(item => item?["externalUserId"]?.GetValue<string>() == request.Subject.ExternalUserId)
                ?? throw new InvalidOperationException($"No REST connector static response exists for '{request.Subject.ExternalUserId}'.");
            var payload = ParseObject(response["payload"], "payload");
            var observedAtUtc = response["observedAtUtc"] switch
            {
                JsonValue value when value.TryGetValue<DateTime>(out var dateTime) => dateTime,
                JsonValue value when value.TryGetValue<string>(out var stringValue) && DateTime.TryParse(stringValue, out var parsed) => parsed,
                _ => DateTime.UtcNow
            };
            return new ConnectorFetchResult(
                payload.ToJsonString(),
                (JsonObject)payload.DeepClone(),
                JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        source = ConnectorType,
                        mode = "staticResponses",
                        request.Subject.ExternalUserId,
                        observedAtUtc
                    }
                }),
                observedAtUtc,
                null,
                "{}");
        }

        var baseUrl = request.Configuration["baseUrl"]?.GetValue<string>()
            ?? throw new InvalidOperationException("REST connector requires baseUrl.");
        var pathTemplate = request.Configuration["pathTemplate"]?.GetValue<string>() ?? string.Empty;
        var subjectQueryParameter = request.Configuration["subjectQueryParameter"]?.GetValue<string>();
        var method = request.Configuration["method"]?.GetValue<string>() ?? "GET";
        var requestUri = BuildUri(baseUrl, pathTemplate, subjectQueryParameter, request.Subject.ExternalUserId);

        var client = httpClientFactory.CreateClient("scout-connectors");
        using var httpRequest = new HttpRequestMessage(new HttpMethod(method), requestUri);
        ApplyHeaders(request.Configuration, request.Credentials, httpRequest);
        using var responseMessage = await client.SendAsync(httpRequest, cancellationToken);
        responseMessage.EnsureSuccessStatusCode();

        var rawJson = await responseMessage.Content.ReadAsStringAsync(cancellationToken);
        var normalizedPayload = ParseObject(JsonNode.Parse(rawJson), "REST payload");
        var resolvedObservedAtUtc = ResolveObservedAt(normalizedPayload, request.Configuration["observedAtPath"]?.GetValue<string>());
        return new ConnectorFetchResult(
            rawJson,
            normalizedPayload,
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    source = ConnectorType,
                    requestUri,
                    request.Subject.ExternalUserId,
                    observedAtUtc = resolvedObservedAtUtc
                }
            }),
            resolvedObservedAtUtc,
            null,
            JsonSerializer.Serialize(new { statusCode = (int)responseMessage.StatusCode }));
    }

    private static void ApplyHeaders(JsonObject configuration, JsonObject credentials, HttpRequestMessage request)
    {
        if (configuration["headers"] is JsonObject headers)
        {
            foreach (var kvp in headers)
            {
                var value = kvp.Value?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    request.Headers.TryAddWithoutValidation(kvp.Key, value);
                }
            }
        }

        if (credentials["bearerToken"]?.GetValue<string>() is { Length: > 0 } bearerToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        if (credentials["apiKey"]?.GetValue<string>() is { Length: > 0 } apiKey)
        {
            var headerName = configuration["apiKeyHeader"]?.GetValue<string>() ?? "X-API-Key";
            request.Headers.TryAddWithoutValidation(headerName, apiKey);
        }

        if (credentials["basicUsername"]?.GetValue<string>() is { Length: > 0 } username
            && credentials["basicPassword"]?.GetValue<string>() is { Length: > 0 } password)
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    private static string BuildUri(string baseUrl, string pathTemplate, string? queryParameter, string externalUserId)
    {
        var uri = baseUrl.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(pathTemplate))
        {
            uri += pathTemplate.Replace("{externalUserId}", Uri.EscapeDataString(externalUserId), StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(queryParameter))
        {
            var separator = uri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            uri += $"{separator}{Uri.EscapeDataString(queryParameter)}={Uri.EscapeDataString(externalUserId)}";
        }

        return uri;
    }

    private static DateTime ResolveObservedAt(JsonObject payload, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DateTime.UtcNow;
        }

        JsonNode? current = payload;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            current = current switch
            {
                JsonObject jsonObject when jsonObject.TryGetPropertyValue(segment, out var child) => child,
                _ => null
            };

            if (current is null)
            {
                return DateTime.UtcNow;
            }
        }

        return current switch
        {
            JsonValue value when value.TryGetValue<DateTime>(out var dateTime) => dateTime,
            JsonValue value when value.TryGetValue<string>(out var stringValue) && DateTime.TryParse(stringValue, out var parsed) => parsed,
            _ => DateTime.UtcNow
        };
    }

    private static bool IsHttpUri(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static void ValidateStaticResponses(JsonArray responses, List<string> errors)
    {
        if (responses.Count == 0)
        {
            errors.Add("REST connector staticResponses must contain at least one response.");
            return;
        }

        for (var index = 0; index < responses.Count; index++)
        {
            if (responses[index] is not JsonObject item)
            {
                errors.Add($"REST connector staticResponses[{index}] must be an object.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(item["externalUserId"]?.GetValue<string>()))
            {
                errors.Add($"REST connector staticResponses[{index}] requires externalUserId.");
            }

            if (item["payload"] is not JsonObject)
            {
                errors.Add($"REST connector staticResponses[{index}] requires payload object.");
            }
        }
    }
}
