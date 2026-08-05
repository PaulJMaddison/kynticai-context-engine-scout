using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace KynticAI.Scout.Sdk;

internal sealed class ScoutHttpPipeline(HttpClient httpClient, ScoutClientOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FactValueTypeJsonConverter() }
    };

    public async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(method, relativePath, body, cancellationToken);
        using var response = await SendWithRetriesAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        return await DeserializeAsync<T>(response, cancellationToken);
    }

    public async Task<T?> SendGraphQlAsync<T>(
        string operationName,
        string query,
        object? variables,
        string dataFieldName,
        CancellationToken cancellationToken)
    {
        var body = new { operationName, query, variables };
        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            ResolveGraphQlPath(),
            body,
            cancellationToken);
        using var response = await SendWithRetriesAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("errors", out var errorsElement) && errorsElement.ValueKind == JsonValueKind.Array && errorsElement.GetArrayLength() > 0)
        {
            var firstError = errorsElement[0];
            var message = firstError.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? "GraphQL request failed."
                : "GraphQL request failed.";
            var correlationId = firstError.TryGetProperty("extensions", out var extensions)
                && extensions.TryGetProperty("correlationId", out var correlation)
                ? correlation.GetString()
                : response.Headers.TryGetValues(options.RequestIdHeaderName, out var values) ? values.FirstOrDefault() : null;
            throw new ScoutException(message, response.StatusCode, correlationId);
        }

        if (!root.TryGetProperty("data", out var dataElement) || !dataElement.TryGetProperty(dataFieldName, out var fieldElement))
        {
            return default;
        }

        if (fieldElement.ValueKind == JsonValueKind.Null)
        {
            return default;
        }

        return fieldElement.Deserialize<T>(JsonOptions);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, BuildUri(relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation(options.RequestIdHeaderName, Guid.NewGuid().ToString("N"));
        request.Headers.TryAddWithoutValidation("User-Agent", options.UserAgent);

        if (options.DefaultHeaders is not null)
        {
            foreach (var header in options.DefaultHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        var token = options.AccessToken;
        if (string.IsNullOrWhiteSpace(token) && options.AccessTokenProvider is not null)
        {
            token = await options.AccessTokenProvider(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendWithRetriesAsync(HttpRequestMessage template, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = await CloneRequestAsync(template, cancellationToken);
            var response = await httpClient.SendAsync(request, cancellationToken);
            if (attempt >= options.MaxRetries || !ShouldRetry(response.StatusCode))
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(ComputeDelay(attempt), cancellationToken);
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout
            || statusCode == HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;

    private TimeSpan ComputeDelay(int attempt)
        => TimeSpan.FromMilliseconds(options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt));

    private async Task<ScoutException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var correlationId = response.Headers.TryGetValues(options.RequestIdHeaderName, out var values)
            ? values.FirstOrDefault()
            : null;

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var errorElement))
            {
                var message = errorElement.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString() ?? $"Request failed with status {(int)response.StatusCode}."
                    : $"Request failed with status {(int)response.StatusCode}.";
                var v1Code = errorElement.TryGetProperty("code", out var v1CodeElement) ? v1CodeElement.GetString() : null;
                var v1CorrelationId = errorElement.TryGetProperty("correlationId", out var v1CorrelationElement)
                    ? v1CorrelationElement.GetString()
                    : null;
                correlationId = v1CorrelationId ?? correlationId;

                var v1Details = new List<ScoutErrorDetail>();
                if (errorElement.TryGetProperty("details", out var detailsElement) && detailsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in detailsElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        foreach (var item in property.Value.EnumerateArray())
                        {
                            v1Details.Add(new ScoutErrorDetail(null, property.Name, item.GetString() ?? "Unknown error."));
                        }
                    }
                }

                return new ScoutException(
                    message,
                    response.StatusCode,
                    correlationId,
                    v1Code,
                    retryable: false,
                    v1Details);
            }

            var title = root.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
            var detail = root.TryGetProperty("detail", out var detailElement) ? detailElement.GetString() : null;
            var code = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
            var retryable = root.TryGetProperty("retryable", out var retryableElement) && retryableElement.GetBoolean();
            var bodyCorrelationId = root.TryGetProperty("correlationId", out var bodyCorrelationIdElement)
                ? bodyCorrelationIdElement.GetString()
                : null;
            correlationId = bodyCorrelationId ?? correlationId;

            var details = new List<ScoutErrorDetail>();
            if (root.TryGetProperty("errors", out var errorsElement) && errorsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in errorsElement.EnumerateArray())
                {
                    details.Add(new ScoutErrorDetail(
                        item.TryGetProperty("code", out var itemCode) ? itemCode.GetString() : null,
                        item.TryGetProperty("target", out var itemTarget) ? itemTarget.GetString() : null,
                        item.TryGetProperty("message", out var itemMessage) ? itemMessage.GetString() ?? "Unknown error." : "Unknown error."));
                }
            }

            return new ScoutException(
                detail ?? title ?? $"Request failed with status {(int)response.StatusCode}.",
                response.StatusCode,
                correlationId,
                code,
                retryable,
                details);
        }
        catch (JsonException)
        {
            return new ScoutException(
                $"Request failed with status {(int)response.StatusCode}.",
                response.StatusCode,
                correlationId);
        }
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new ScoutException("The response body was empty.");
    }

    private async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage template, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(template.Method, template.RequestUri);
        foreach (var header in template.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (template.Content is not null)
        {
            var content = await template.Content.ReadAsStringAsync(cancellationToken);
            clone.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        return clone;
    }

    private string ResolveGraphQlPath()
    {
        if (!string.IsNullOrWhiteSpace(options.GraphQlEndpoint))
        {
            return options.GraphQlEndpoint!;
        }

        return "/graphql";
    }

    private Uri BuildUri(string relativePath)
    {
        if (Uri.TryCreate(relativePath, UriKind.Absolute, out var absolute)
            && absolute.Scheme != Uri.UriSchemeFile)
        {
            return absolute;
        }

        var baseUrl = options.BaseUrl.TrimEnd('/');
        var path = relativePath.StartsWith('/') ? relativePath : "/" + relativePath;
        if (baseUrl.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            path = path["/api/v1".Length..];
            if (path.Length == 0)
            {
                path = "/";
            }
        }

        return new Uri(baseUrl + path, UriKind.Absolute);
    }
}
