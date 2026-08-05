using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using KynticAI.Scout.Sdk;

namespace KynticAI.Scout.Sdk.Tests;

/// <summary>
/// Contract tests for the <see cref="FactValueType"/> wire format. The API
/// serialises the enum as an integer (System.Text.Json default), and the SDK
/// must read that integer encoding and also tolerate the string encoding for
/// backward compatibility. These tests pin the integer wire value of every
/// enum member so the API and SDK sides cannot silently drift again.
/// </summary>
public sealed class FactValueTypeContractTests
{
    private static readonly JsonSerializerOptions ApiOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions SdkOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FactValueTypeJsonConverter() }
    };

    [Theory]
    [InlineData(FactValueType.String, 1)]
    [InlineData(FactValueType.Number, 2)]
    [InlineData(FactValueType.Boolean, 3)]
    [InlineData(FactValueType.Json, 4)]
    [InlineData(FactValueType.Enum, 5)]
    [InlineData(FactValueType.EnumSet, 6)]
    public void FactValueType_IntegerWireValue_IsPinnedOnApiSide(FactValueType value, int expectedWireValue)
    {
        var wire = JsonSerializer.Serialize(value, ApiOptions);

        Assert.Equal(expectedWireValue, int.Parse(wire, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(FactValueType.String, 1)]
    [InlineData(FactValueType.Number, 2)]
    [InlineData(FactValueType.Boolean, 3)]
    [InlineData(FactValueType.Json, 4)]
    [InlineData(FactValueType.Enum, 5)]
    [InlineData(FactValueType.EnumSet, 6)]
    public void FactValueType_IntegerWireValue_IsPinnedOnSdkSide(FactValueType value, int expectedWireValue)
    {
        var wire = JsonSerializer.Serialize(value, SdkOptions);

        Assert.Equal(expectedWireValue, int.Parse(wire, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(FactValueType.String)]
    [InlineData(FactValueType.Number)]
    [InlineData(FactValueType.Boolean)]
    [InlineData(FactValueType.Json)]
    [InlineData(FactValueType.Enum)]
    [InlineData(FactValueType.EnumSet)]
    public void FactValueType_StringEncoding_IsTolerated(FactValueType value)
    {
        var stringWire = JsonSerializer.Serialize(value.ToString(), ApiOptions);
        var deserialized = JsonSerializer.Deserialize<FactValueType>(stringWire, SdkOptions);

        Assert.Equal(value, deserialized);
    }

    [Theory]
    [InlineData("\"Number\"", FactValueType.Number)]
    [InlineData("\"number\"", FactValueType.Number)]
    [InlineData("\"ENUM\"", FactValueType.Enum)]
    [InlineData("\"EnumSet\"", FactValueType.EnumSet)]
    public void FactValueType_StringEncoding_VariantNamesAreTolerated(string wire, FactValueType expected)
    {
        var deserialized = JsonSerializer.Deserialize<FactValueType>(wire, SdkOptions);

        Assert.Equal(expected, deserialized);
    }

    [Fact]
    public void FactValueType_IntegerEncoding_RoundTripsThroughSdkOptions()
    {
        foreach (var value in Enum.GetValues<FactValueType>())
        {
            var wire = JsonSerializer.Serialize(value, SdkOptions);
            var deserialized = JsonSerializer.Deserialize<FactValueType>(wire, SdkOptions);

            Assert.Equal(value, deserialized);
        }
    }

    [Fact]
    public void FactValueType_UnknownStringEncoding_Throws()
    {
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<FactValueType>("\"DATETIME\"", SdkOptions));

        Assert.Contains("FactValueType", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContextFactResult_ValueType_ReadsIntegerWireFormat_ThroughSdkPipeline()
    {
        var handler = new StubHttpMessageHandler(_ =>
            Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, """
            {
              "items": [
                {
                  "id": "8e22fcf4-6640-4fba-8992-14bd208b89fa",
                  "attributeKey": "conversionProbability",
                  "valueJson": "0.85",
                  "valueType": 2,
                  "confidence": 0.92,
                  "observedAtUtc": "2026-05-11T10:00:00Z",
                  "freshUntilUtc": null,
                  "sourceSelectorDefinitionId": "0f864ac6-dcbf-4850-bd98-1d13975d7813",
                  "explanation": "Conversion probability mapped from CRM conversion score.",
                  "provenanceJson": "[]"
                },
                {
                  "id": "a1a1a1a1-b2b2-c3c3-d4d4-a1a1a1a1a1a1",
                  "attributeKey": "churnRisk",
                  "valueJson": "\"low\"",
                  "valueType": 5,
                  "confidence": 0.88,
                  "observedAtUtc": "2026-05-11T10:00:00Z",
                  "freshUntilUtc": null,
                  "sourceSelectorDefinitionId": "0f864ac6-dcbf-4850-bd98-1d13975d7813",
                  "explanation": "Churn risk classified from CRM churn score.",
                  "provenanceJson": "[]"
                }
              ],
              "page": 1,
              "pageSize": 50,
              "totalCount": 2,
              "hasMore": false
            }
            """)));

        using var httpClient = new HttpClient(handler);
        using var client = new ScoutClient(httpClient, new ScoutClientOptions
        {
            BaseUrl = "http://127.0.0.1:5198",
            AccessToken = "token-123"
        });

        var facts = await client.Facts.GetForUserAsync("demo", "123");

        Assert.Equal(2, facts.Count);
        Assert.Equal(FactValueType.Number, facts[0].ValueType);
        Assert.Equal(FactValueType.Enum, facts[1].ValueType);
    }

    [Fact]
    public async Task GroundedContextFactResult_ValueType_ReadsIntegerWireFormat_ThroughSdkPipeline()
    {
        var handler = new StubHttpMessageHandler(_ =>
            Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, """
            {
              "snapshotId": "8e22fcf4-6640-4fba-8992-14bd208b89fa",
              "tenantSlug": "demo",
              "externalUserId": "123",
              "fullName": "Avery Stone",
              "companyName": "Larkspur Logistics Group",
              "jobTitle": "VP Revenue",
              "segment": "enterprise",
              "salesObjective": "Prepare a renewal-risk brief.",
              "summary": "Grounded context package.",
              "overallConfidence": 0.91,
              "generatedAtUtc": "2026-05-11T10:00:00Z",
              "isStale": false,
              "humanReviewRecommended": true,
              "missingInformation": [],
              "weakSignalMessages": [],
              "facts": [
                {
                  "citationId": "FACT-01",
                  "factId": "8e22fcf4-6640-4fba-8992-14bd208b89fa",
                  "attributeKey": "conversionProbability",
                  "displayName": "Conversion Probability",
                  "valueJson": "85",
                  "valueType": 2,
                  "confidence": 0.93,
                  "observedAtUtc": "2026-05-11T10:00:00Z",
                  "freshUntilUtc": null,
                  "isFresh": true,
                  "isLowConfidence": false,
                  "explanation": "Conversion probability is high.",
                  "provenanceJson": "[]"
                },
                {
                  "citationId": "FACT-02",
                  "factId": "a1a1a1a1-b2b2-c3c3-d4d4-a1a1a1a1a1a1",
                  "attributeKey": "planInterest",
                  "displayName": "Plan Interest",
                  "valueJson": "\"enterprise\"",
                  "valueType": 5,
                  "confidence": 0.91,
                  "observedAtUtc": "2026-05-11T10:00:00Z",
                  "freshUntilUtc": null,
                  "isFresh": true,
                  "isLowConfidence": false,
                  "explanation": "Enterprise plan interest is explicit.",
                  "provenanceJson": "[]"
                }
              ],
              "contextPackageJson": "{}"
            }
            """)));

        using var httpClient = new HttpClient(handler);
        using var client = new ScoutClient(httpClient, new ScoutClientOptions
        {
            BaseUrl = "http://127.0.0.1:5198",
            AccessToken = "token-123"
        });

        var result = await client.Packages.GetAiContextForUserAsync("demo", "123", "Prepare a renewal-risk brief.");

        Assert.NotNull(result);
        Assert.Equal(2, result!.Facts.Count);
        Assert.Equal(FactValueType.Number, result.Facts[0].ValueType);
        Assert.Equal(FactValueType.Enum, result.Facts[1].ValueType);
    }

    [Fact]
    public void ContextFactResult_ValueType_SerialisesAsInteger()
    {
        var fact = new ContextFactResult(
            Guid.Parse("8e22fcf4-6640-4fba-8992-14bd208b89fa"),
            "conversionProbability",
            "0.85",
            FactValueType.Number,
            0.92m,
            DateTime.Parse("2026-05-11T10:00:00Z"),
            null,
            Guid.Parse("0f864ac6-dcbf-4850-bd98-1d13975d7813"),
            "Conversion probability mapped from CRM conversion score.",
            "[]");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(fact, SdkOptions));
        var valueType = document.RootElement.GetProperty("valueType");

        Assert.Equal(JsonValueKind.Number, valueType.ValueKind);
        Assert.Equal((int)FactValueType.Number, valueType.GetInt32());
    }

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string json)
        => new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }
}
