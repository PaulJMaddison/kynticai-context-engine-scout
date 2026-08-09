using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Contracts;

namespace KynticAI.Scout.Application.Validation;

public sealed record UclContractValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static UclContractValidationResult Valid { get; } = new(true, []);

    public static UclContractValidationResult Invalid(IReadOnlyList<string> errors) => new(false, errors);
}

public static class UclEvidencePackV1Validator
{
    public static UclContractValidationResult Validate(UclEvidencePackV1 package)
    {
        var errors = new List<string>();
        if (package.PackageKind != UclEvidencePackContractVersions.EvidencePackKind)
        {
            errors.Add("packageKind must be ucl.evidence-pack.");
        }

        if (package.PackageVersion != UclEvidencePackContractVersions.EvidencePackV1)
        {
            errors.Add("packageVersion must be ucl.evidence-pack.v1.");
        }

        if (package.DataPlane != UclEvidencePackContractVersions.CustomerOwnedDataPlane)
        {
            errors.Add("dataPlane must be customer-owned-data-plane.");
        }

        if (!package.Governance.RawDataRetainedInCustomerDataPlane)
        {
            errors.Add("governance.rawDataRetainedInCustomerDataPlane must be true.");
        }

        var provenanceIds = package.Provenance
            .Select(x => x.CitationId)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        if (provenanceIds.Count != package.Provenance.Count)
        {
            errors.Add("provenance citation IDs must be present and unique.");
        }

        foreach (var record in package.ExactLinkedRecords.Records)
        {
            if (!provenanceIds.Contains(record.CitationId))
            {
                errors.Add($"exactLinkedRecords record {record.RecordType}/{record.RecordId} uses citation {record.CitationId} without matching provenance.");
                continue;
            }

            if (!package.Provenance.Any(x =>
                    x.CitationId == record.CitationId
                    && x.SourceEntityType == record.RecordType
                    && x.SourceEntityId == record.RecordId))
            {
                errors.Add($"exactLinkedRecords record {record.RecordType}/{record.RecordId} citation {record.CitationId} does not match provenance source.");
            }
        }

        foreach (var citationId in CollectReferencedCitationIds(package).Where(static x => !string.IsNullOrWhiteSpace(x)))
        {
            if (!provenanceIds.Contains(citationId))
            {
                errors.Add($"citation {citationId} is referenced by derived intelligence but is missing from provenance.");
            }
        }

        return errors.Count == 0
            ? UclContractValidationResult.Valid
            : UclContractValidationResult.Invalid(errors);
    }

    private static IEnumerable<string> CollectReferencedCitationIds(UclEvidencePackV1 package)
    {
        foreach (var record in package.ExactLinkedRecords.Records)
        {
            yield return record.CitationId;
        }

        foreach (var relationship in package.Relationships)
        {
            foreach (var citationId in relationship.CitationIds)
            {
                yield return citationId;
            }
        }

        foreach (var pattern in package.SimilarWonLostPatterns)
        {
            foreach (var citationId in pattern.CitationIds)
            {
                yield return citationId;
            }
        }

        foreach (var signal in package.WeightedSignals)
        {
            foreach (var citationId in signal.CitationIds)
            {
                yield return citationId;
            }
        }

        foreach (var citationId in package.RecommendedAction.CitationIds)
        {
            yield return citationId;
        }

        if (package.DraftResponse is not null)
        {
            foreach (var citationId in package.DraftResponse.CitationIds)
            {
                yield return citationId;
            }
        }
    }
}

public static class UclCloudAggregateUsageV1Validator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> RootAllowList = new(StringComparer.Ordinal)
    {
        "payloadKind",
        "payloadVersion",
        "packageVersion",
        "tenantSlug",
        "feature",
        "eventName",
        "status",
        "generatedAtUtc",
        "featureUsageCounters",
        "controlPlaneCounters",
        "dataBoundary"
    };

    private static readonly HashSet<string> FeatureCounterAllowList = new(StringComparer.Ordinal)
    {
        "nextActionGenerateRequests",
        "dataPlanePackageBuilds"
    };

    private static readonly HashSet<string> ControlPlaneCounterAllowList = new(StringComparer.Ordinal)
    {
        "appliedRuleCount",
        "maskedFieldCount",
        "deniedFieldCount"
    };

    private static readonly HashSet<string> DataBoundaryAllowList = new(StringComparer.Ordinal)
    {
        "rawDataRetainedInCustomerDataPlane",
        "containsRawCustomerData",
        "containsRecords",
        "containsFacts",
        "containsContextFacts",
        "containsSnapshots",
        "containsContextSnapshots",
        "containsEvidencePacks",
        "containsPrompts",
        "containsGeneratedContent",
        "containsRecommendations",
        "containsCitations",
        "containsCitationIds",
        "containsRelationshipTypes",
        "containsWeightedSignals",
        "containsCaveats",
        "containsPerEntityRelationshipMetadata",
        "containsDerivedRelationshipIntelligence",
        "containsPerCustomerDerivedIntelligence"
    };

    private static readonly HashSet<string> ForbiddenPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "records",
        "rawRecords",
        "sourceRecords",
        "facts",
        "contextFacts",
        "snapshots",
        "contextSnapshots",
        "evidencePack",
        "evidencePacks",
        "localEvidencePack",
        "localDerivedEvidencePackageJson",
        "prompt",
        "prompts",
        "generatedContent",
        "recommendation",
        "recommendations",
        "recommendedAction",
        "recommendedNextAction",
        "citation",
        "citations",
        "citationId",
        "citationIds",
        "relationship",
        "relationships",
        "relationshipType",
        "relationshipTypes",
        "weightedSignal",
        "weightedSignals",
        "caveat",
        "caveats",
        "confidence",
        "subject",
        "subjectIdentifier",
        "hashedSubjectIdentifier",
        "subjectHash",
        "objective",
        "purpose",
        "purposeCategory",
        "purposeHash",
        "actorRole",
        "sourcePayload",
        "sourceEntityId",
        "recordId",
        "externalAccountId",
        "accountName",
        "accountDomain",
        "email",
        "relationshipMetadata",
        "perEntityRelationshipMetadata",
        "derivedRelationshipIntelligence",
        "perCustomerDerivedIntelligence"
    };

    public static UclContractValidationResult Validate(UclCloudAggregateUsageV1 payload)
        => ValidateJson(JsonSerializer.Serialize(payload, JsonOptions));

    public static UclContractValidationResult ValidateJson(string payloadJson)
    {
        var errors = new List<string>();
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(payloadJson) as JsonObject;
        }
        catch (JsonException ex)
        {
            return UclContractValidationResult.Invalid([$"payload is not valid JSON: {ex.Message}"]);
        }

        if (root is null)
        {
            return UclContractValidationResult.Invalid(["payload root must be a JSON object."]);
        }

        foreach (var property in root)
        {
            if (!RootAllowList.Contains(property.Key))
            {
                errors.Add($"root property '{property.Key}' is not allowed in UclCloudAggregateUsageV1.");
            }
        }

        RequireString(root, "payloadKind", UclEvidencePackContractVersions.CloudAggregateUsageKind, errors);
        RequireString(root, "payloadVersion", UclEvidencePackContractVersions.CloudAggregateUsageV1, errors);
        RequireString(root, "packageVersion", UclEvidencePackContractVersions.EvidencePackV1, errors);
        RequireNonEmptyString(root, "tenantSlug", errors);
        RequireNonEmptyString(root, "feature", errors);
        RequireNonEmptyString(root, "eventName", errors);
        RequireNonEmptyString(root, "status", errors);
        RequireDateTime(root, "generatedAtUtc", errors);

        ValidateIntegerObject(root["featureUsageCounters"], "featureUsageCounters", FeatureCounterAllowList, errors);
        ValidateIntegerObject(root["controlPlaneCounters"], "controlPlaneCounters", ControlPlaneCounterAllowList, errors);
        ValidateDataBoundary(root["dataBoundary"], errors);
        RejectForbiddenPropertyNames(root, "$", errors);

        return errors.Count == 0
            ? UclContractValidationResult.Valid
            : UclContractValidationResult.Invalid(errors);
    }

    private static void ValidateIntegerObject(
        JsonNode? node,
        string path,
        HashSet<string> allowList,
        List<string> errors)
    {
        if (node is not JsonObject obj)
        {
            errors.Add($"{path} must be an object.");
            return;
        }

        foreach (var property in obj)
        {
            if (!allowList.Contains(property.Key))
            {
                errors.Add($"{path}.{property.Key} is not allowed.");
                continue;
            }

            if (!TryGetInt32(property.Value, out var value) || value < 0)
            {
                errors.Add($"{path}.{property.Key} must be a non-negative integer.");
            }
        }

        foreach (var required in allowList)
        {
            if (!obj.ContainsKey(required))
            {
                errors.Add($"{path}.{required} is required.");
            }
        }
    }

    private static void ValidateDataBoundary(JsonNode? node, List<string> errors)
    {
        if (node is not JsonObject obj)
        {
            errors.Add("dataBoundary must be an object.");
            return;
        }

        foreach (var property in obj)
        {
            if (!DataBoundaryAllowList.Contains(property.Key))
            {
                errors.Add($"dataBoundary.{property.Key} is not allowed.");
                continue;
            }

            if (!TryGetBoolean(property.Value, out var value))
            {
                errors.Add($"dataBoundary.{property.Key} must be boolean.");
                continue;
            }

            if (property.Key == "rawDataRetainedInCustomerDataPlane")
            {
                if (!value)
                {
                    errors.Add("dataBoundary.rawDataRetainedInCustomerDataPlane must be true.");
                }
            }
            else if (value)
            {
                errors.Add($"dataBoundary.{property.Key} must be false for Cloud aggregate usage payloads.");
            }
        }

        foreach (var required in DataBoundaryAllowList)
        {
            if (!obj.ContainsKey(required))
            {
                errors.Add($"dataBoundary.{required} is required.");
            }
        }
    }

    private static void RejectForbiddenPropertyNames(JsonNode? node, string path, List<string> errors)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                var propertyPath = $"{path}.{property.Key}";
                if (!IsAllowedDataBoundaryFlag(path, property.Key) && ForbiddenPropertyNames.Contains(property.Key))
                {
                    errors.Add($"{propertyPath} is forbidden in Cloud aggregate usage payloads.");
                }

                RejectForbiddenPropertyNames(property.Value, propertyPath, errors);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                RejectForbiddenPropertyNames(array[index], $"{path}[{index}]", errors);
            }
        }
    }

    private static bool IsAllowedDataBoundaryFlag(string path, string propertyName)
        => path == "$.dataBoundary" && DataBoundaryAllowList.Contains(propertyName);

    private static void RequireString(JsonObject root, string propertyName, string expectedValue, List<string> errors)
    {
        if (!TryGetString(root[propertyName], out var value) || value != expectedValue)
        {
            errors.Add($"{propertyName} must be '{expectedValue}'.");
        }
    }

    private static void RequireNonEmptyString(JsonObject root, string propertyName, List<string> errors)
    {
        if (!TryGetString(root[propertyName], out var value) || string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{propertyName} must be a non-empty string.");
        }
    }

    private static void RequireDateTime(JsonObject root, string propertyName, List<string> errors)
    {
        if (!TryGetString(root[propertyName], out var value) || !DateTime.TryParse(value, out _))
        {
            errors.Add($"{propertyName} must be an ISO 8601 timestamp.");
        }
    }

    private static bool TryGetString(JsonNode? node, out string value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue(out string? candidate) && candidate is not null)
        {
            value = candidate;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetInt32(JsonNode? node, out int value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue(out int candidate))
        {
            value = candidate;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryGetBoolean(JsonNode? node, out bool value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue(out bool candidate))
        {
            value = candidate;
            return true;
        }

        value = false;
        return false;
    }
}

public static class UclScoutLocalContextBriefV1Validator
{
    private static readonly HashSet<string> RequiredActions = new(StringComparer.Ordinal)
    {
        "email_customer_with_x_offer",
        "offer_prize_draw_registration",
        "do_not_reply"
    };

    public static UclContractValidationResult ValidateJson(string briefJson)
    {
        var errors = new List<string>();
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(briefJson) as JsonObject;
        }
        catch (JsonException ex)
        {
            return UclContractValidationResult.Invalid([$"brief is not valid JSON: {ex.Message}"]);
        }

        if (root is null)
        {
            return UclContractValidationResult.Invalid(["brief root must be a JSON object."]);
        }

        RequireString(root, "brief_kind", UclEvidencePackContractVersions.ScoutLocalContextBriefKind, errors);
        RequireString(root, "brief_version", UclEvidencePackContractVersions.ScoutLocalContextBriefV1, errors);
        RequireNonEmptyString(root, "brief_id", errors);
        RequireDateTime(root, "generated_at", errors);
        ValidateSubject(root["subject"], errors);
        ValidateSourceScope(root["source_scope"], errors);
        var evidenceIds = ValidateEvidence(root["evidence"], errors);
        ValidateRelationshipContext(root["relationship_context"], evidenceIds, errors);
        ValidateActions(root["action_options"], evidenceIds, errors);
        ValidateDoNotActions(root["do_not_action_options"], evidenceIds, errors);
        ValidateHandoff(root["handoff"], errors);

        return errors.Count == 0
            ? UclContractValidationResult.Valid
            : UclContractValidationResult.Invalid(errors);
    }

    private static void ValidateSubject(JsonNode? node, List<string> errors)
    {
        if (node is not JsonObject obj)
        {
            errors.Add("subject must be an object.");
            return;
        }

        RequireNonEmptyString(obj, "entity_id", errors);
        RequireNonEmptyString(obj, "entity_type", errors);
    }

    private static void ValidateSourceScope(JsonNode? node, List<string> errors)
    {
        if (node is not JsonObject obj)
        {
            errors.Add("source_scope must be an object.");
            return;
        }

        RequireString(obj, "data_plane", "customer-owned", errors);
        RequireBoolean(obj, "synthetic", expected: true, errors);
        RequireBoolean(obj, "authorised_for_local_proof", expected: true, errors);
        ValidateStringArray(obj["approved_sources"], "source_scope.approved_sources", errors);
        if (obj["approved_sources"] is JsonArray sources && sources.Count < 4)
        {
            errors.Add("source_scope.approved_sources must include the synthetic web, email, interest, registration, and outcome source set.");
        }
    }

    private static HashSet<string> ValidateEvidence(JsonNode? node, List<string> errors)
    {
        var evidenceIds = new HashSet<string>(StringComparer.Ordinal);
        if (node is not JsonArray evidence || evidence.Count == 0)
        {
            errors.Add("evidence must be a non-empty array.");
            return evidenceIds;
        }

        foreach (var item in evidence)
        {
            if (item is not JsonObject obj)
            {
                errors.Add("evidence entries must be objects.");
                continue;
            }

            if (!TryGetString(obj["evidence_id"], out var evidenceId) || string.IsNullOrWhiteSpace(evidenceId))
            {
                errors.Add("evidence.evidence_id is required.");
                continue;
            }

            if (!evidenceIds.Add(evidenceId))
            {
                errors.Add($"evidence ID {evidenceId} is duplicated.");
            }

            RequireNonEmptyString(obj, "source_type", errors);
            RequireNonEmptyString(obj, "source_id", errors);
            RequireNonEmptyString(obj, "summary", errors);
            RequireBoolean(obj, "exact_item_stored_locally", expected: true, errors);
            RequireDateTime(obj, "observed_at", errors);
        }

        return evidenceIds;
    }

    private static void ValidateRelationshipContext(JsonNode? node, HashSet<string> evidenceIds, List<string> errors)
    {
        if (node is not JsonObject obj)
        {
            errors.Add("relationship_context must be an object.");
            return;
        }

        ValidateNonEmptyObjectArray(obj["matched_journeys"], "relationship_context.matched_journeys", errors);
        ValidateCitationArray(obj["attribution_paths"], "relationship_context.attribution_paths", "evidence_ids", evidenceIds, errors);
        RequireNonEmptyString(obj, "outcome_window", errors);
        RequireString(obj, "probability_scope", "Scout local/basic proof only; Fortress owns canonical production scoring.", errors);
    }

    private static void ValidateActions(JsonNode? node, HashSet<string> evidenceIds, List<string> errors)
    {
        if (node is not JsonArray actions || actions.Count == 0)
        {
            errors.Add("action_options must be a non-empty array.");
            return;
        }

        var seenActions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in actions)
        {
            if (item is not JsonObject obj)
            {
                errors.Add("action_options entries must be objects.");
                continue;
            }

            if (TryGetString(obj["action"], out var action))
            {
                seenActions.Add(action);
            }
            else
            {
                errors.Add("action_options.action is required.");
            }

            RequireNonNegativeInteger(obj, "rank", errors);
            RequireDecimalRange(obj, "estimated_success_probability", 0m, 1m, errors);
            RequireString(obj, "probability_label", "local_basic_proof_not_fortress_canonical", errors);
            ValidateCitationIds(obj["evidence_ids"], "action_options.evidence_ids", evidenceIds, errors);
            ValidateStringArray(obj["caveats"], "action_options.caveats", errors);
        }

        foreach (var required in RequiredActions)
        {
            if (!seenActions.Contains(required))
            {
                errors.Add($"action_options must include {required}.");
            }
        }
    }

    private static void ValidateDoNotActions(JsonNode? node, HashSet<string> evidenceIds, List<string> errors)
    {
        if (node is not JsonArray actions || actions.Count == 0)
        {
            errors.Add("do_not_action_options must be a non-empty array.");
            return;
        }

        foreach (var item in actions)
        {
            if (item is not JsonObject obj)
            {
                errors.Add("do_not_action_options entries must be objects.");
                continue;
            }

            RequireString(obj, "action", "do_not_reply", errors);
            RequireDecimalRange(obj, "estimated_no_further_contact_probability", 0m, 1m, errors);
            RequireString(obj, "probability_label", "local_basic_proof_not_fortress_canonical", errors);
            ValidateCitationIds(obj["evidence_ids"], "do_not_action_options.evidence_ids", evidenceIds, errors);
            ValidateStringArray(obj["caveats"], "do_not_action_options.caveats", errors);
        }
    }

    private static void ValidateHandoff(JsonNode? node, List<string> errors)
    {
        if (node is not JsonObject obj)
        {
            errors.Add("handoff must be an object.");
            return;
        }

        RequireNonEmptyString(obj, "approved_consumer", errors);
        RequireBoolean(obj, "model_may_explain_not_infer_evidence", expected: true, errors);
        RequireBoolean(obj, "fortress_required_for_canonical_scoring", expected: true, errors);
    }

    private static void ValidateNonEmptyObjectArray(JsonNode? node, string path, List<string> errors)
    {
        if (node is not JsonArray array || array.Count == 0)
        {
            errors.Add($"{path} must be a non-empty array.");
            return;
        }

        foreach (var item in array)
        {
            if (item is not JsonObject)
            {
                errors.Add($"{path} entries must be objects.");
            }
        }
    }

    private static void ValidateCitationArray(
        JsonNode? node,
        string path,
        string citationProperty,
        HashSet<string> evidenceIds,
        List<string> errors)
    {
        if (node is not JsonArray array || array.Count == 0)
        {
            errors.Add($"{path} must be a non-empty array.");
            return;
        }

        foreach (var item in array)
        {
            if (item is not JsonObject obj)
            {
                errors.Add($"{path} entries must be objects.");
                continue;
            }

            ValidateCitationIds(obj[citationProperty], $"{path}.{citationProperty}", evidenceIds, errors);
        }
    }

    private static void ValidateCitationIds(JsonNode? node, string path, HashSet<string> evidenceIds, List<string> errors)
    {
        if (node is not JsonArray citationIds || citationIds.Count == 0)
        {
            errors.Add($"{path} must be a non-empty array.");
            return;
        }

        foreach (var item in citationIds)
        {
            if (!TryGetString(item, out var evidenceId) || string.IsNullOrWhiteSpace(evidenceId))
            {
                errors.Add($"{path} entries must be strings.");
                continue;
            }

            if (!evidenceIds.Contains(evidenceId))
            {
                errors.Add($"{path} cites {evidenceId} without matching evidence.");
            }
        }
    }

    private static void ValidateStringArray(JsonNode? node, string path, List<string> errors)
    {
        if (node is not JsonArray array || array.Count == 0)
        {
            errors.Add($"{path} must be a non-empty array.");
            return;
        }

        foreach (var item in array)
        {
            if (!TryGetString(item, out var value) || string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{path} entries must be non-empty strings.");
            }
        }
    }

    private static void RequireString(JsonObject root, string propertyName, string expectedValue, List<string> errors)
    {
        if (!TryGetString(root[propertyName], out var value) || value != expectedValue)
        {
            errors.Add($"{propertyName} must be '{expectedValue}'.");
        }
    }

    private static void RequireNonEmptyString(JsonObject root, string propertyName, List<string> errors)
    {
        if (!TryGetString(root[propertyName], out var value) || string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{propertyName} must be a non-empty string.");
        }
    }

    private static void RequireBoolean(JsonObject root, string propertyName, bool expected, List<string> errors)
    {
        if (root[propertyName] is not JsonValue value || !value.TryGetValue(out bool actual) || actual != expected)
        {
            errors.Add($"{propertyName} must be {expected.ToString().ToLowerInvariant()}.");
        }
    }

    private static void RequireDateTime(JsonObject root, string propertyName, List<string> errors)
    {
        if (!TryGetString(root[propertyName], out var value) || !DateTime.TryParse(value, out _))
        {
            errors.Add($"{propertyName} must be an ISO 8601 timestamp.");
        }
    }

    private static void RequireNonNegativeInteger(JsonObject root, string propertyName, List<string> errors)
    {
        if (root[propertyName] is not JsonValue value || !value.TryGetValue(out int actual) || actual < 0)
        {
            errors.Add($"{propertyName} must be a non-negative integer.");
        }
    }

    private static void RequireDecimalRange(JsonObject root, string propertyName, decimal minimum, decimal maximum, List<string> errors)
    {
        if (root[propertyName] is not JsonValue value || !value.TryGetValue(out decimal actual) || actual < minimum || actual > maximum)
        {
            errors.Add($"{propertyName} must be between {minimum} and {maximum}.");
        }
    }

    private static bool TryGetString(JsonNode? node, out string value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue(out string? candidate) && candidate is not null)
        {
            value = candidate;
            return true;
        }

        value = string.Empty;
        return false;
    }
}

public static class UclEnterpriseRelationshipEngineHandoffV1Validator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> ForbiddenPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "canonicalWeight",
        "canonicalWeights",
        "enterpriseWeight",
        "enterpriseWeights",
        "privateWeight",
        "privateWeights",
        "weightFormula",
        "canonicalFormula",
        "scoringConfig",
        "rustScoringConfig",
        "lanceDb",
        "lanceDbPath",
        "embedding",
        "embeddings",
        "vectorPipeline"
    };

    public static UclContractValidationResult Validate(UclEnterpriseRelationshipEngineHandoffV1 artifact)
        => ValidateJson(JsonSerializer.Serialize(artifact, JsonOptions));

    public static UclContractValidationResult ValidateJson(string artifactJson)
    {
        var errors = new List<string>();
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(artifactJson) as JsonObject;
        }
        catch (JsonException ex)
        {
            return UclContractValidationResult.Invalid([$"artifact is not valid JSON: {ex.Message}"]);
        }

        if (root is null)
        {
            return UclContractValidationResult.Invalid(["artifact root must be a JSON object."]);
        }

        RequireString(root, "artifactKind", UclEvidencePackContractVersions.EnterpriseRelationshipEngineHandoffKind, errors);
        RequireString(root, "artifactVersion", UclEvidencePackContractVersions.EnterpriseRelationshipEngineHandoffV1, errors);
        RequireNonEmptyString(root, "handoffId", errors);
        RequireString(root, "packageKind", UclEvidencePackContractVersions.EvidencePackKind, errors);
        RequireString(root, "packageVersion", UclEvidencePackContractVersions.EvidencePackV1, errors);
        RequireNonEmptyString(root, "packageId", errors);
        RequireDateTime(root, "generatedAtUtc", errors);
        RequireNonEmptyString(root, "tenantSlug", errors);
        RequireString(root, "dataPlane", UclEvidencePackContractVersions.CustomerOwnedDataPlane, errors);
        RequireNonEmptyString(root, "producer", errors);
        RequireString(root, "fallbackEngine", "BasicRelationshipEngine", errors);
        RequireBoolean(root, "requiresLiveEnterpriseService", expected: false, errors);
        RequireBoolean(root, "enterpriseOnlyInternalsIncluded", expected: false, errors);
        ValidateRelationshipWeighting(root["relationshipWeighting"], errors);
        ValidateSubject(root["subject"], errors);
        RequireNonEmptyString(root, "objective", errors);
        RequireNonEmptyString(root, "purpose", errors);
        RequireNonEmptyString(root, "actorRole", errors);
        ValidateEvidenceSummary(root["evidenceSummary"], errors);
        var provenanceIds = ValidateProvenance(root["provenance"], errors);
        ValidateCandidateRelationships(root["candidateRelationships"], provenanceIds, errors);
        ValidateRequiredEnterpriseOutputs(root["requiredEnterpriseOutputs"], errors);
        RejectForbiddenPropertyNames(root, "$", errors);

        return errors.Count == 0
            ? UclContractValidationResult.Valid
            : UclContractValidationResult.Invalid(errors);
    }

    private static void ValidateRelationshipWeighting(JsonNode? node, List<string> errors)
    {
        if (node is not JsonObject obj)
        {
            errors.Add("relationshipWeighting must be an object.");
            return;
        }

        RequireString(obj, "scope", UclDataItemAttributionContractVersions.BasicFallbackOnlyScope, errors);
        RequireBoolean(obj, "scoutWeightsAreCanonical", expected: false, errors);
        RequireString(obj, "canonicalOwner", "Enterprise", errors);
        RequireNonEmptyString(obj, "canonicalEngine", errors);
    }

    private static void ValidateSubject(JsonNode? node, List<string> errors)
    {
        if (node is not JsonObject obj)
        {
            errors.Add("subject must be an object.");
            return;
        }

        RequireNonEmptyString(obj, "subjectType", errors);
        RequireNonEmptyString(obj, "subjectIdentifier", errors);
        RequireNonEmptyString(obj, "externalAccountId", errors);
        if (obj["primaryContactId"] is not null && !TryGetString(obj["primaryContactId"], out _))
        {
            errors.Add("subject.primaryContactId must be a string or null.");
        }
    }

    private static void ValidateEvidenceSummary(JsonNode? node, List<string> errors)
    {
        if (node is not JsonObject obj)
        {
            errors.Add("evidenceSummary must be an object.");
            return;
        }

        if (obj["recordCounts"] is not JsonObject)
        {
            errors.Add("evidenceSummary.recordCounts must be an object.");
        }

        RequireNonNegativeInteger(obj, "exactRecordCount", errors);
        RequireNonNegativeInteger(obj, "candidateRelationshipCount", errors);
        RequireNonNegativeInteger(obj, "provenanceCitationCount", errors);
    }

    private static HashSet<string> ValidateProvenance(JsonNode? node, List<string> errors)
    {
        var provenanceIds = new HashSet<string>(StringComparer.Ordinal);
        if (node is not JsonArray provenance || provenance.Count == 0)
        {
            errors.Add("provenance must be a non-empty array.");
            return provenanceIds;
        }

        foreach (var item in provenance)
        {
            if (item is not JsonObject obj)
            {
                errors.Add("provenance entries must be objects.");
                continue;
            }

            if (!TryGetString(obj["citationId"], out var citationId) || string.IsNullOrWhiteSpace(citationId))
            {
                errors.Add("provenance.citationId is required.");
                continue;
            }

            if (!provenanceIds.Add(citationId))
            {
                errors.Add($"provenance citation ID {citationId} is duplicated.");
            }

            RequireNonEmptyString(obj, "sourceEntityType", errors);
            RequireNonEmptyString(obj, "sourceEntityId", errors);
            RequireNonEmptyString(obj, "evidenceType", errors);
        }

        return provenanceIds;
    }

    private static void ValidateCandidateRelationships(JsonNode? node, HashSet<string> provenanceIds, List<string> errors)
    {
        if (node is not JsonArray relationships || relationships.Count == 0)
        {
            errors.Add("candidateRelationships must be a non-empty array.");
            return;
        }

        foreach (var item in relationships)
        {
            if (item is not JsonObject obj)
            {
                errors.Add("candidateRelationships entries must be objects.");
                continue;
            }

            RequireNonEmptyString(obj, "relationshipId", errors);
            RequireNonEmptyString(obj, "relationshipType", errors);
            RequireNonEmptyString(obj, "linkKind", errors);
            RequireNonEmptyString(obj, "sourceType", errors);
            RequireNonEmptyString(obj, "sourceId", errors);
            RequireNonEmptyString(obj, "targetType", errors);
            RequireNonEmptyString(obj, "targetId", errors);
            RequireDecimalRange(obj, "confidence", 0m, 1m, errors);
            RequireDecimal(obj, "scoutFallbackWeight", errors);
            RequireString(obj, "fallbackWeightScope", UclDataItemAttributionContractVersions.BasicFallbackOnlyScope, errors);
            RequireNonEmptyString(obj, "rationale", errors);
            ValidateCitationIds(obj["citationIds"], provenanceIds, errors);
        }
    }

    private static void ValidateCitationIds(JsonNode? node, HashSet<string> provenanceIds, List<string> errors)
    {
        if (node is not JsonArray citationIds || citationIds.Count == 0)
        {
            errors.Add("candidateRelationships.citationIds must be a non-empty array.");
            return;
        }

        foreach (var item in citationIds)
        {
            if (!TryGetString(item, out var citationId) || string.IsNullOrWhiteSpace(citationId))
            {
                errors.Add("candidateRelationships.citationIds entries must be strings.");
                continue;
            }

            if (!provenanceIds.Contains(citationId))
            {
                errors.Add($"candidate relationship cites {citationId} without matching provenance.");
            }
        }
    }

    private static void ValidateRequiredEnterpriseOutputs(JsonNode? node, List<string> errors)
    {
        if (node is not JsonArray outputs || outputs.Count == 0)
        {
            errors.Add("requiredEnterpriseOutputs must be a non-empty array.");
            return;
        }

        var outputNames = outputs
            .Where(item => TryGetString(item, out _))
            .Select(item => item!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var required in new[] { "canonicalRelationshipWeights", "canonicalTraversalSignals" })
        {
            if (!outputNames.Contains(required))
            {
                errors.Add($"requiredEnterpriseOutputs must include {required}.");
            }
        }
    }

    private static void RejectForbiddenPropertyNames(JsonNode? node, string path, List<string> errors)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                var propertyPath = $"{path}.{property.Key}";
                if (ForbiddenPropertyNames.Contains(property.Key))
                {
                    errors.Add($"{propertyPath} is forbidden in Enterprise relationship handoff artifacts.");
                }

                RejectForbiddenPropertyNames(property.Value, propertyPath, errors);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                RejectForbiddenPropertyNames(array[index], $"{path}[{index}]", errors);
            }
        }
    }

    private static void RequireString(JsonObject root, string propertyName, string expectedValue, List<string> errors)
    {
        if (!TryGetString(root[propertyName], out var value) || value != expectedValue)
        {
            errors.Add($"{propertyName} must be '{expectedValue}'.");
        }
    }

    private static void RequireNonEmptyString(JsonObject root, string propertyName, List<string> errors)
    {
        if (!TryGetString(root[propertyName], out var value) || string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{propertyName} must be a non-empty string.");
        }
    }

    private static void RequireBoolean(JsonObject root, string propertyName, bool expected, List<string> errors)
    {
        if (root[propertyName] is not JsonValue value || !value.TryGetValue(out bool actual) || actual != expected)
        {
            errors.Add($"{propertyName} must be {expected.ToString().ToLowerInvariant()}.");
        }
    }

    private static void RequireDateTime(JsonObject root, string propertyName, List<string> errors)
    {
        if (!TryGetString(root[propertyName], out var value) || !DateTime.TryParse(value, out _))
        {
            errors.Add($"{propertyName} must be an ISO 8601 timestamp.");
        }
    }

    private static void RequireNonNegativeInteger(JsonObject root, string propertyName, List<string> errors)
    {
        if (root[propertyName] is not JsonValue value || !value.TryGetValue(out int actual) || actual < 0)
        {
            errors.Add($"{propertyName} must be a non-negative integer.");
        }
    }

    private static void RequireDecimal(JsonObject root, string propertyName, List<string> errors)
    {
        if (root[propertyName] is not JsonValue value || !value.TryGetValue(out decimal _))
        {
            errors.Add($"{propertyName} must be a number.");
        }
    }

    private static void RequireDecimalRange(JsonObject root, string propertyName, decimal minimum, decimal maximum, List<string> errors)
    {
        if (root[propertyName] is not JsonValue value || !value.TryGetValue(out decimal actual) || actual < minimum || actual > maximum)
        {
            errors.Add($"{propertyName} must be between {minimum} and {maximum}.");
        }
    }

    private static bool TryGetString(JsonNode? node, out string value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue(out string? candidate) && candidate is not null)
        {
            value = candidate;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
