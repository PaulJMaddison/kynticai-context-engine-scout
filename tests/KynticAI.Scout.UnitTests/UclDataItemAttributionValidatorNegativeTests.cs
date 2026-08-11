using System.Text.Json;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Application.Validation;

namespace KynticAI.Scout.UnitTests;

public sealed class UclDataItemAttributionValidatorNegativeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly DateTime FixedUtc = new(2026, 6, 14, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DataItems_WithDuplicateDataItemId_AreRejected()
    {
        var first = SampleDataItem("item-dupe", "email_address");
        var second = SampleDataItem("item-dupe", "web_event");

        var result = UclDataItemAttributionV1Validator.ValidateDataItems([first, second]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("item-dupe is duplicated", StringComparison.Ordinal));
    }

    [Fact]
    public void DataItems_WithoutIdentities_AreRejected()
    {
        var item = SampleDataItem("item-no-identities", "web_event", identities: []);

        var result = UclDataItemAttributionV1Validator.ValidateDataItems([item]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("identities must contain at least one", StringComparison.Ordinal));
    }

    [Fact]
    public void DataItems_WithNonObjectExactPayload_AreRejected()
    {
        var item = SampleDataItem("item-array-payload", "web_event") with
        {
            ExactPayload = JsonSerializer.SerializeToElement(new[] { 1, 2, 3 })
        };

        var result = UclDataItemAttributionV1Validator.ValidateDataItems([item]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("exactPayload must be an object", StringComparison.Ordinal));
    }

    [Fact]
    public void RelationshipSet_WithUnknownSubjectDataItem_IsRejected()
    {
        var dataItems = SampleDataItems(3);
        var relationshipSet = SampleRelationshipSet(dataItems, "set-unknown-subject") with
        {
            SubjectDataItemId = "item-does-not-exist"
        };

        var result = UclDataItemAttributionV1Validator.ValidateRelationshipSets([relationshipSet], dataItems);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("references unknown data item item-does-not-exist", StringComparison.Ordinal));
    }

    [Fact]
    public void AttributionPath_WithOutOfOrderSequence_IsRejected()
    {
        var dataItems = SampleDataItems(3);
        var relationshipSet = SampleRelationshipSet(dataItems, "set-out-of-order-sequence");
        var path = relationshipSet.AttributionPaths[0];
        var events = path.Events.ToList();
        events.Reverse();

        var result = UclDataItemAttributionV1Validator.ValidateRelationshipSets(
            [relationshipSet with { AttributionPaths = [path with { Events = events }] }],
            dataItems);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("strictly increasing sequence", StringComparison.Ordinal));
    }

    [Fact]
    public void AttributionPath_WithOutOfOrderTimestamps_IsRejected()
    {
        var dataItems = SampleDataItems(3);
        var relationshipSet = SampleRelationshipSet(dataItems, "set-out-of-order-timestamps");
        var path = relationshipSet.AttributionPaths[0];
        var events = path.Events
            .Select((item, index) => item with
            {
                OccurredAtUtc = path.Events[^1].OccurredAtUtc.AddMinutes(-index)
            })
            .ToList();

        var result = UclDataItemAttributionV1Validator.ValidateRelationshipSets(
            [relationshipSet with { AttributionPaths = [path with { Events = events }] }],
            dataItems);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("must preserve observed event order", StringComparison.Ordinal));
    }

    [Fact]
    public void AttributionPath_WithUnknownEventDataItem_IsRejected()
    {
        var dataItems = SampleDataItems(3);
        var relationshipSet = SampleRelationshipSet(dataItems, "set-unknown-event-item");
        var path = relationshipSet.AttributionPaths[0];
        var events = path.Events
            .Select((item, index) => index == 0
                ? item with { DataItemId = "item-unknown-event" }
                : item)
            .ToList();

        var result = UclDataItemAttributionV1Validator.ValidateRelationshipSets(
            [relationshipSet with { AttributionPaths = [path with { Events = events }] }],
            dataItems);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("references unknown data item item-unknown-event", StringComparison.Ordinal));
    }

    [Fact]
    public void Edge_WithConfidenceOutsideZeroToOne_IsRejected()
    {
        var dataItems = SampleDataItems(3);
        var relationshipSet = SampleRelationshipSet(dataItems, "set-confidence-out-of-range");
        var edge = relationshipSet.Edges[0] with { Confidence = 1.01m };

        var result = UclDataItemAttributionV1Validator.ValidateRelationshipSets(
            [relationshipSet with { Edges = [edge] }],
            dataItems);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("confidence must be between 0 and 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Edge_WithUnknownCitationDataItem_IsRejected()
    {
        var dataItems = SampleDataItems(3);
        var relationshipSet = SampleRelationshipSet(dataItems, "set-unknown-citation");
        var edge = relationshipSet.Edges[0] with { CitationDataItemIds = ["item-unknown-citation"] };

        var result = UclDataItemAttributionV1Validator.ValidateRelationshipSets(
            [relationshipSet with { Edges = [edge] }],
            dataItems);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("references unknown data item item-unknown-citation", StringComparison.Ordinal));
    }

    [Fact]
    public void Outcome_WithUnknownDataItem_IsRejected()
    {
        var dataItems = SampleDataItems(3);
        var relationshipSet = SampleRelationshipSet(dataItems, "set-unknown-outcome");
        var outcome = new OutcomeEvent(
            "outcome-001",
            "item-unknown-outcome",
            "converted",
            Converted: true,
            OutcomeValue: 1m,
            FixedUtc.AddHours(2),
            []);

        var result = UclDataItemAttributionV1Validator.ValidateRelationshipSets(
            [relationshipSet with { HistoricalOutcomes = [outcome] }],
            dataItems);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("references unknown data item item-unknown-outcome", StringComparison.Ordinal));
    }

    [Fact]
    public void RelationshipSet_WithEnterpriseScope_IsRejectedAsNotPublicFallback()
    {
        var dataItems = SampleDataItems(3);
        var relationshipSet = SampleRelationshipSet(dataItems, "set-enterprise-scope") with
        {
            AnalysisScope = "enterprise-canonical"
        };

        var result = UclDataItemAttributionV1Validator.ValidateRelationshipSets([relationshipSet], dataItems);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("analysisScope must be basic-public-fallback-only", StringComparison.Ordinal));
    }

    [Fact]
    public void EnterpriseInput_RequiringCloudControlPlane_IsRejected()
    {
        var dataItems = SampleDataItems(3);
        var relationshipSets = SampleRelationshipSets(dataItems);
        var input = new EnterpriseRelationshipAnalysisInput(
            UclDataItemAttributionContractVersions.EnterpriseRelationshipAnalysisInputKind,
            UclDataItemAttributionContractVersions.EnterpriseRelationshipAnalysisInputV1,
            "input-001",
            FixedUtc,
            "demo",
            UclDataItemAttributionContractVersions.CustomerOwnedDataPlane,
            "KynticAI Scout",
            UclDataItemAttributionContractVersions.BasicFallbackOnlyScope,
            CloudControlPlaneRequired: true,
            EnterpriseOnlyInternalsIncluded: false,
            dataItems,
            relationshipSets,
            ["rankedRelationshipSets", "attributionPathComparisons", "bestNextActionOptions"]);

        var result = UclDataItemAttributionV1Validator.ValidateEnterpriseInput(input);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("cloudControlPlaneRequired must be false", StringComparison.Ordinal));
    }

    [Fact]
    public void EnterpriseInput_MissingRequiredEnterpriseOutputs_IsRejected()
    {
        var dataItems = SampleDataItems(3);
        var relationshipSets = SampleRelationshipSets(dataItems);
        var input = new EnterpriseRelationshipAnalysisInput(
            UclDataItemAttributionContractVersions.EnterpriseRelationshipAnalysisInputKind,
            UclDataItemAttributionContractVersions.EnterpriseRelationshipAnalysisInputV1,
            "input-002",
            FixedUtc,
            "demo",
            UclDataItemAttributionContractVersions.CustomerOwnedDataPlane,
            "KynticAI Scout",
            UclDataItemAttributionContractVersions.BasicFallbackOnlyScope,
            CloudControlPlaneRequired: false,
            EnterpriseOnlyInternalsIncluded: false,
            dataItems,
            relationshipSets,
            ["rankedRelationshipSets"]);

        var result = UclDataItemAttributionV1Validator.ValidateEnterpriseInput(input);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("requiredEnterpriseOutputs must include attributionPathComparisons", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("requiredEnterpriseOutputs must include bestNextActionOptions", StringComparison.Ordinal));
    }

    [Fact]
    public void CloudPayload_WithForbiddenIdentityProperty_IsRejected()
    {
        var unsafePayload = """
            {
              "payloadKind": "ucl.cloud-aggregate-control-plane-payload",
              "payloadVersion": "ucl.cloud-aggregate-control-plane-payload.v1",
              "tenantSlug": "demo",
              "feature": "relationship-item-attribution",
              "eventName": "ucl.relationship-items.generated",
              "status": "succeeded",
              "generatedAtUtc": "2026-06-14T09:15:00Z",
              "counters": {
                "dataItemCount": 1,
                "relationshipSetCount": 1,
                "attributionPathCount": 1,
                "historicalOutcomeCount": 0,
                "possibleActionCount": 1
              },
              "dataBoundary": {
                "rawDataRetainedInCustomerDataPlane": true,
                "containsRawCustomerData": false,
                "containsDataItems": false,
                "containsExactPayloads": false,
                "containsIdentities": false,
                "containsRelationshipEdges": false,
                "containsAttributionPaths": false,
                "containsOutcomeEvents": false,
                "containsEnterpriseAnalysisInput": false
              },
              "identities": [{ "identityType": "email", "identityValue": "testname@test.com" }]
            }
            """;

        var result = UclDataItemAttributionV1Validator.ValidateCloudPayloadJson(unsafePayload);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("root property 'identities' is not allowed", StringComparison.Ordinal));
    }

    [Fact]
    public void CloudPayload_WithMissingRequiredCounter_IsRejected()
    {
        var unsafePayload = """
            {
              "payloadKind": "ucl.cloud-aggregate-control-plane-payload",
              "payloadVersion": "ucl.cloud-aggregate-control-plane-payload.v1",
              "tenantSlug": "demo",
              "feature": "relationship-item-attribution",
              "eventName": "ucl.relationship-items.generated",
              "status": "succeeded",
              "generatedAtUtc": "2026-06-14T09:15:00Z",
              "counters": {
                "dataItemCount": 1
              },
              "dataBoundary": {
                "rawDataRetainedInCustomerDataPlane": true,
                "containsRawCustomerData": false,
                "containsDataItems": false,
                "containsExactPayloads": false,
                "containsIdentities": false,
                "containsRelationshipEdges": false,
                "containsAttributionPaths": false,
                "containsOutcomeEvents": false,
                "containsEnterpriseAnalysisInput": false
              }
            }
            """;

        var result = UclDataItemAttributionV1Validator.ValidateCloudPayloadJson(unsafePayload);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("counters.relationshipSetCount is required", StringComparison.Ordinal));
    }

    [Fact]
    public void CloudPayload_WithInvalidDataBoundaryFlag_IsRejected()
    {
        var unsafePayload = """
            {
              "payloadKind": "ucl.cloud-aggregate-control-plane-payload",
              "payloadVersion": "ucl.cloud-aggregate-control-plane-payload.v1",
              "tenantSlug": "demo",
              "feature": "relationship-item-attribution",
              "eventName": "ucl.relationship-items.generated",
              "status": "succeeded",
              "generatedAtUtc": "2026-06-14T09:15:00Z",
              "counters": {
                "dataItemCount": 1,
                "relationshipSetCount": 1,
                "attributionPathCount": 1,
                "historicalOutcomeCount": 0,
                "possibleActionCount": 1
              },
              "dataBoundary": {
                "rawDataRetainedInCustomerDataPlane": true,
                "containsRawCustomerData": true,
                "containsDataItems": false,
                "containsExactPayloads": false,
                "containsIdentities": false,
                "containsRelationshipEdges": false,
                "containsAttributionPaths": false,
                "containsOutcomeEvents": false,
                "containsEnterpriseAnalysisInput": false
              }
            }
            """;

        var result = UclDataItemAttributionV1Validator.ValidateCloudPayloadJson(unsafePayload);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("dataBoundary.containsRawCustomerData must be false", StringComparison.Ordinal));
    }

    private static IReadOnlyList<DataItem> SampleDataItems(int count)
        => Enumerable.Range(1, count)
            .Select(index => SampleDataItem($"item-{index}", index % 2 == 0 ? "web_event" : "product_browse_search"))
            .ToList();

    private static DataItem SampleDataItem(
        string dataItemId,
        string dataItemType,
        IReadOnlyList<DataItemIdentity>? identities = null)
        => new(
            UclDataItemAttributionContractVersions.DataItemKind,
            UclDataItemAttributionContractVersions.DataItemV1,
            dataItemId,
            dataItemType,
            "connector",
            "demo-web",
            $"source-{dataItemId}",
            FixedUtc,
            FixedUtc.AddMinutes(1),
            UclDataItemAttributionContractVersions.CustomerOwnedDataPlane,
            identities ?? [new DataItemIdentity("cookie", "cookie-1", "cookie-1", IsPrimary: true, "customer-data-plane")],
            JsonSerializer.SerializeToElement(new { id = dataItemId }, JsonOptions));

    private static IReadOnlyList<RelationshipSet> SampleRelationshipSets(IReadOnlyList<DataItem> dataItems)
        => [SampleRelationshipSet(dataItems, "set-001")];

    private static RelationshipSet SampleRelationshipSet(
        IReadOnlyList<DataItem> dataItems,
        string relationshipSetId)
    {
        var events = dataItems
            .Select((item, index) => new AttributionEvent(
                $"event-{index + 1}",
                item.DataItemId,
                index + 1,
                index == 0 ? "page_a_search" : "product_b_interest",
                FixedUtc.AddMinutes(index),
                $"Event {index + 1}"))
            .ToList();

        return new RelationshipSet(
            UclDataItemAttributionContractVersions.RelationshipSetKind,
            UclDataItemAttributionContractVersions.RelationshipSetV1,
            relationshipSetId,
            dataItems[0].DataItemId,
            "conversion",
            UclDataItemAttributionContractVersions.BasicFallbackOnlyScope,
            [
                new RelationshipEdge(
                    $"{relationshipSetId}-edge-1",
                    "SameCookie",
                    "identity",
                    dataItems[0].DataItemId,
                    dataItems[^1].DataItemId,
                    "cookie",
                    "cookie-1",
                    1.0m,
                    [dataItems[0].DataItemId])
            ],
            [
                new AttributionPath(
                    $"{relationshipSetId}-path-1",
                    "cookie",
                    "cookie-1",
                    "conversion",
                    events,
                    ["follow-up email"],
                    null)
            ],
            [
                new OutcomeEvent(
                    $"{relationshipSetId}-outcome-1",
                    dataItems[^1].DataItemId,
                    "converted",
                    Converted: true,
                    OutcomeValue: 1m,
                    FixedUtc.AddHours(2),
                    [dataItems[^1].DataItemId])
            ]);
    }
}
