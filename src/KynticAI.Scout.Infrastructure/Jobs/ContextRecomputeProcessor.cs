using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KynticAI.Scout.Infrastructure.Jobs;

internal sealed class ContextRecomputeProcessor(
    ScoutDbContext dbContext,
    IClock clock,
    ISelectorExecutionEngine selectorExecutionEngine,
    ILogger<ContextRecomputeProcessor> logger)
{
    public async Task ProcessAsync(ContextRecomputeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tenant = await dbContext.Tenants.FirstAsync(x => x.Id == request.TenantId, cancellationToken);
        var user = await dbContext.UserProfiles.FirstAsync(
            x => x.Id == request.UserProfileId && x.TenantId == request.TenantId,
            cancellationToken);
        var recomputeJob = await dbContext.RecomputeJobs
            .FirstOrDefaultAsync(x => x.TenantId == request.TenantId && x.CorrelationId == request.CorrelationId, cancellationToken);
        if (recomputeJob is not null)
        {
            if (recomputeJob.Status is RecomputeJobStatus.Completed or RecomputeJobStatus.Failed)
            {
                return;
            }

            if (recomputeJob.UserProfileId != request.UserProfileId)
            {
                recomputeJob.MarkFailed(
                    "Persisted recompute job does not match the queued user.",
                    JsonSerializer.Serialize(new { request.CorrelationId, request.UserProfileId }),
                    clock.UtcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException("Recompute request does not match its persisted job.");
            }

            recomputeJob.MarkRunning(clock.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var requestedExecutionIds = request.SelectorExecutionIds.Distinct().ToList();
        var executions = await dbContext.SelectorExecutions
            .Include(x => x.SelectorDefinition)
                .ThenInclude(x => x.TargetAttributeDefinition)
            .Include(x => x.SelectorDefinition)
                .ThenInclude(x => x.DataSource)
            .Where(x => x.TenantId == request.TenantId
                && x.UserProfileId == request.UserProfileId
                && requestedExecutionIds.Contains(x.Id))
            .OrderBy(x => x.RequestedAtUtc)
            .ToListAsync(cancellationToken);

        if (executions.Count != requestedExecutionIds.Count)
        {
            recomputeJob?.MarkFailed(
                "One or more selector executions are missing or do not belong to this recompute job.",
                JsonSerializer.Serialize(new
                {
                    request.CorrelationId,
                    requested = requestedExecutionIds.Count,
                    resolved = executions.Count
                }),
                clock.UtcNow);
            if (recomputeJob is not null)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            throw new InvalidOperationException("Recompute request references missing or mismatched selector executions.");
        }

        var successfulFacts = new List<SelectorCandidateFact>();
        foreach (var execution in executions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (execution.Status == SelectorExecutionStatus.Succeeded)
            {
                var restored = RestoreSuccessfulCandidate(execution);
                if (restored is null)
                {
                    execution.MarkFailed(
                        "Persisted successful selector execution is missing or contains invalid result state.",
                        execution.RawSourceDataJson,
                        execution.ValidationErrorsJson,
                        execution.PipelineTraceJson,
                        clock.UtcNow);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    successfulFacts.Add(restored);
                }
                continue;
            }

            if (execution.Status == SelectorExecutionStatus.Failed)
            {
                continue;
            }

            execution.MarkRunning(clock.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);

            var selector = execution.SelectorDefinition;
            var dataSource = selector.DataSource;
            if (dataSource is null)
            {
                execution.MarkFailed(
                    $"Selector '{selector.Name}' does not reference a data source.",
                    "{}",
                    "[]",
                    JsonSerializer.Serialize(new { selector = selector.Name, error = "Missing data source." }),
                    clock.UtcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
                continue;
            }

            var runtimeContext = new SelectorRuntimeContext(selector, dataSource, selector.TargetAttributeDefinition);
            var outcome = await selectorExecutionEngine.ExecuteAsync(runtimeContext, user, execution.ExecutionMode, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (outcome.IsSuccess && outcome.CandidateFact is { } candidateFact)
            {
                execution.MarkSucceeded(
                    candidateFact.ValueJson,
                    candidateFact.ValueType,
                    candidateFact.Confidence,
                    candidateFact.ObservedAtUtc,
                    candidateFact.Explanation,
                    candidateFact.ProvenanceJson,
                    candidateFact.RawSourceDataJson,
                    candidateFact.ValidationErrorsJson,
                    candidateFact.PipelineTraceJson,
                    clock.UtcNow);
                successfulFacts.Add(candidateFact);
                dbContext.ProvenanceMetadata.Add(CreateProvenanceRecord(
                    tenant.Id,
                    execution.Id,
                    null,
                    "selector-execution",
                    candidateFact.AttributeKey,
                    candidateFact.ObservedAtUtc,
                    candidateFact.ProvenanceJson));
            }
            else
            {
                var errorMessage = outcome.ValidationErrors.Count > 0
                    ? string.Join("; ", outcome.ValidationErrors)
                    : $"Selector '{selector.Name}' did not produce a value.";
                execution.MarkFailed(
                    errorMessage,
                    outcome.RawSourceDataJson,
                    JsonSerializer.Serialize(outcome.ValidationErrors),
                    outcome.PipelineTraceJson,
                    clock.UtcNow);
                logger.LogWarning("Selector {SelectorId} failed for user {ExternalUserId}: {ErrorMessage}", execution.SelectorDefinitionId, user.ExternalUserId, errorMessage);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (successfulFacts.Count == 0)
        {
            recomputeJob?.MarkFailed(
                "No selectors produced facts.",
                JsonSerializer.Serialize(new { request.CorrelationId, request.UserProfileId }),
                clock.UtcNow);
            dbContext.AuditEvents.Add(AuditEvent.Create(
                tenant.Id,
                "system",
                "context.recompute.failed",
                nameof(UserProfile),
                user.Id.ToString("D"),
                request.CorrelationId,
                JsonSerializer.Serialize(new { reason = "No selectors produced facts." }),
                null,
                null,
                clock.UtcNow));
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var resolvedFacts = ResolveConflicts(successfulFacts);
        var previousSnapshot = await dbContext.ContextSnapshots
            .Where(x => x.TenantId == tenant.Id && x.UserProfileId == user.Id && !x.IsStale)
            .OrderByDescending(x => x.GeneratedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (previousSnapshot is not null)
        {
            previousSnapshot.MarkStale(clock.UtcNow);
        }

        var snapshot = ContextSnapshot.Create(
            tenant.Id,
            user.Id,
            (previousSnapshot?.SnapshotVersion ?? 0) + 1,
            BuildSummary(resolvedFacts),
            Math.Round(resolvedFacts.Average(x => x.Confidence), 4),
            clock.UtcNow);

        if (resolvedFacts.Any(x => x.FreshUntilUtc.HasValue && x.FreshUntilUtc.Value < clock.UtcNow))
        {
            snapshot.MarkStale(clock.UtcNow);
        }

        dbContext.ContextSnapshots.Add(snapshot);
        foreach (var fact in resolvedFacts)
        {
            var contextFact = ContextFact.Create(
                tenant.Id,
                snapshot.Id,
                fact.AttributeDefinitionId,
                fact.SelectorDefinitionId,
                fact.AttributeKey,
                fact.ValueJson,
                fact.ValueType,
                fact.Confidence,
                fact.ObservedAtUtc,
                fact.FreshUntilUtc,
                fact.Explanation,
                fact.ProvenanceJson,
                clock.UtcNow);
            dbContext.ContextFacts.Add(contextFact);
            dbContext.ProvenanceMetadata.Add(CreateProvenanceRecord(
                tenant.Id,
                null,
                contextFact.Id,
                "context-fact",
                fact.AttributeKey,
                fact.ObservedAtUtc,
                fact.ProvenanceJson));
        }

        recomputeJob?.MarkCompleted(
            snapshot.Summary,
            JsonSerializer.Serialize(new
            {
                snapshotId = snapshot.Id,
                snapshotVersion = snapshot.SnapshotVersion,
                factCount = resolvedFacts.Count
            }),
            clock.UtcNow);
        dbContext.AuditEvents.Add(AuditEvent.Create(
            tenant.Id,
            "system",
            "context.recompute.completed",
            nameof(ContextSnapshot),
            snapshot.Id.ToString("D"),
            request.CorrelationId,
            JsonSerializer.Serialize(new
            {
                tenant = tenant.Slug,
                user = user.ExternalUserId,
                snapshotVersion = snapshot.SnapshotVersion,
                factCount = resolvedFacts.Count
            }),
            null,
            JsonSerializer.Serialize(new { snapshot.Summary, snapshot.OverallConfidence, snapshot.IsStale }),
            clock.UtcNow));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static SelectorCandidateFact? RestoreSuccessfulCandidate(SelectorExecution execution)
    {
        if (execution.Status != SelectorExecutionStatus.Succeeded
            || execution.ResultObservedAtUtc is not { } observedAtUtc
            || string.IsNullOrWhiteSpace(execution.ResultValueJson)
            || string.IsNullOrWhiteSpace(execution.ResultProvenanceJson)
            || !IsValidJson(execution.ResultValueJson))
        {
            return null;
        }

        var selector = execution.SelectorDefinition;
        var target = selector.TargetAttributeDefinition;
        return new SelectorCandidateFact(
            selector.Id,
            target.Id,
            target.Key,
            execution.ResultValueJson,
            execution.ResultValueType,
            execution.ResultConfidence,
            observedAtUtc,
            observedAtUtc.AddMinutes(selector.FreshnessWindowMinutes),
            execution.ResultExplanation,
            execution.ResultProvenanceJson,
            execution.RawSourceDataJson,
            execution.RawSourceDataJson,
            execution.ValidationErrorsJson,
            execution.PipelineTraceJson,
            selector.Priority);
    }

    private static ProvenanceMetadata CreateProvenanceRecord(
        Guid tenantId,
        Guid? selectorExecutionId,
        Guid? contextFactId,
        string kind,
        string sourceRecordKey,
        DateTime observedAtUtc,
        string provenanceJson)
    {
        var sourceSystem = "unknown";
        try
        {
            if (JsonNode.Parse(provenanceJson) is JsonObject provenanceObject)
            {
                sourceSystem = ResolveSourceSystem(provenanceObject) ?? sourceSystem;
            }
        }
        catch (JsonException)
        {
            // Retain the original provenance even when older/corrupt metadata cannot be parsed for
            // the convenience source-system field. Provenance parsing must not lose the recompute.
        }

        return ProvenanceMetadata.Create(
            tenantId,
            selectorExecutionId,
            contextFactId,
            kind,
            sourceSystem,
            sourceRecordKey,
            provenanceJson,
            observedAtUtc,
            observedAtUtc);
    }

    private static string? ResolveSourceSystem(JsonObject provenance)
    {
        if (TryGetString(provenance["connectorType"], out var connectorType))
        {
            return connectorType;
        }

        if (provenance["source"] is JsonObject sourceObject
            && TryGetString(sourceObject["source"], out var objectSource))
        {
            return objectSource;
        }

        if (provenance["source"] is JsonArray sourceArray)
        {
            foreach (var sourceNode in sourceArray)
            {
                if (sourceNode is JsonObject item
                    && TryGetString(item["source"], out var arraySource))
                {
                    return arraySource;
                }
            }
        }

        return provenance["selector"] is JsonObject selector
            && TryGetString(selector["name"], out var selectorName)
                ? selectorName
                : null;
    }

    private static bool TryGetString(JsonNode? node, [NotNullWhen(true)] out string? value)
    {
        if (node is JsonValue jsonValue
            && jsonValue.TryGetValue<string>(out var parsed)
            && !string.IsNullOrWhiteSpace(parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static bool IsValidJson(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<SelectorCandidateFact> ResolveConflicts(IReadOnlyList<SelectorCandidateFact> candidates)
    {
        var resolved = new List<SelectorCandidateFact>();
        foreach (var attributeGroup in candidates.GroupBy(x => x.AttributeKey, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = attributeGroup
                .OrderByDescending(x => x.Priority)
                .ThenByDescending(x => x.Confidence)
                .ThenByDescending(x => x.ObservedAtUtc)
                .ToList();

            var winner = ordered[0];
            if (ordered.Count > 1)
            {
                winner = winner with
                {
                    ProvenanceJson = AppendConflictResolution(winner.ProvenanceJson, ordered)
                };
            }

            resolved.Add(winner);
        }

        return resolved;
    }

    private static string AppendConflictResolution(string provenanceJson, IReadOnlyList<SelectorCandidateFact> candidates)
    {
        JsonObject provenance;
        try
        {
            provenance = JsonNode.Parse(provenanceJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            provenance = new JsonObject
            {
                ["unparsedOriginalProvenance"] = provenanceJson
            };
        }

        provenance["conflictResolution"] = JsonSerializer.SerializeToNode(new
        {
            strategy = "priority-confidence-observedAt",
            chosenSelectorDefinitionId = candidates[0].SelectorDefinitionId,
            competingSelectors = candidates.Skip(1).Select(candidate => new
            {
                candidate.SelectorDefinitionId,
                candidate.Confidence,
                candidate.Priority,
                candidate.ObservedAtUtc,
                candidate.ValueJson
            })
        });
        return provenance.ToJsonString();
    }

    private static string BuildSummary(IReadOnlyCollection<SelectorCandidateFact> facts)
    {
        if (facts.Count == 0)
        {
            return "No context facts resolved.";
        }

        var attributeKeys = facts
            .Select(fact => fact.AttributeKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return $"Resolved {facts.Count} context fact{(facts.Count == 1 ? string.Empty : "s")}: {string.Join(", ", attributeKeys)}.";
    }
}
