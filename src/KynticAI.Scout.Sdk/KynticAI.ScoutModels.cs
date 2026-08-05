namespace KynticAI.Scout.Sdk;

public sealed record LoginRequest(string TenantSlug, string Email, string Password);

public sealed record AuthenticatedOperator(
    Guid TenantId,
    string TenantSlug,
    Guid OperatorAccountId,
    string Email,
    string DisplayName,
    string Role);

public sealed record AuthSession(
    string AccessToken,
    DateTime ExpiresAtUtc,
    AuthenticatedOperator Operator);

public sealed record MachineTokenRequest(
    string GrantType,
    string ClientId,
    string ClientSecret,
    string? Scope);

public sealed record MachineTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string Scope);

public sealed record UserContextLookupInput(string TenantSlug, string ExternalUserId);

public sealed record SalesContextPackageInput(string TenantSlug, string ExternalUserId, string SalesObjective);

public sealed record QueueContextRecomputeInput(string TenantSlug, string ExternalUserId, string TriggeredBy);

public sealed record PublishSelectorDefinitionInput(string TenantSlug, Guid SelectorDefinitionId);

public sealed record RunScheduledRecomputeInput(string? TenantSlug);

public sealed record PreviewSelectorInput(
    string TenantSlug,
    string ExternalUserId,
    Guid? SelectorDefinitionId,
    UpsertSelectorDefinitionInput? DraftSelector);

public sealed record ValidateSelectorInput(
    string TenantSlug,
    UpsertSelectorDefinitionInput DraftSelector,
    string? ExternalUserId);

public sealed record UpsertSelectorDefinitionInput(
    Guid? Id,
    string TenantSlug,
    Guid? DataSourceId,
    Guid TargetAttributeDefinitionId,
    string Name,
    string Description,
    string MappingKind,
    string ExpressionJson,
    string ExplanationTemplate,
    string ValidationSchemaJson,
    decimal DefaultConfidence,
    int FreshnessWindowMinutes,
    int Priority,
    int? ScheduleIntervalMinutes);

public sealed record ContextFactResult(
    Guid Id,
    string AttributeKey,
    string ValueJson,
    FactValueType ValueType,
    decimal Confidence,
    DateTime ObservedAtUtc,
    DateTime? FreshUntilUtc,
    Guid SourceSelectorDefinitionId,
    string Explanation,
    string ProvenanceJson);

public sealed record ContextFactLookupOptions(
    string? AttributeKey = null,
    int? Page = null,
    int? PageSize = null);

public sealed record PageResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore);

public sealed record SourceSystemEventRequest(
    string? EventId,
    string? WorkspaceSlug,
    string SourceSystem,
    string EventType,
    object? Payload,
    string? PayloadJson,
    string? ExternalUserId,
    string? ExternalAccountId,
    DateTime? ObservedAtUtc);

public sealed record SourceSystemEventAcceptedResult(
    string EventId,
    Guid TenantId,
    string TenantSlug,
    Guid? WorkspaceId,
    Guid? UserProfileId,
    int StoredSignalCount,
    int MatchedSelectorCount,
    string Status,
    bool IsDuplicate,
    DateTime AcceptedAtUtc);

public sealed record ConnectorPluginDefinitionResult(
    string ConnectorType,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> SupportedDataSourceKinds,
    IReadOnlyList<string> SupportedCapabilities,
    string ConfigurationSchemaJson,
    string CredentialSchemaJson,
    string SampleConfigurationJson);

public sealed record ConnectorCatalogueEntryResult(
    string ConnectorType,
    string DisplayName,
    string Description,
    string Category,
    string PublicStatus,
    string Availability,
    bool IsIncludedInOpenCore,
    bool RequiresCommercialAgreement,
    bool IsPlaceholder,
    bool IsEnabled,
    IReadOnlyList<string> SupportedDataSourceKinds,
    IReadOnlyList<string> Capabilities,
    string ConfigurationSchemaJson,
    string CredentialSchemaJson,
    string HealthCheckMode);

public sealed record OperationalHighlightResult(string Label, string Value, string Explanation);

public sealed record OperationalTimelineEventResult(string Category, string Description, DateTime OccurredAtUtc);

public sealed record OperationalSourceSummaryResult(
    string ExternalAccountId,
    string AccountName,
    string Domain,
    string Industry,
    string Region,
    string LifecycleStage,
    string ActivePlanName,
    string SubscriptionStatus,
    decimal MonthlyRecurringRevenue,
    int OpenOpportunities,
    int OpenSupportTickets,
    int PricingPageVisits30d,
    int ActiveDays30,
    int EmailReplies30d,
    IReadOnlyList<OperationalHighlightResult> Highlights,
    IReadOnlyList<OperationalTimelineEventResult> RecentTimeline,
    string RawSummaryJson);

public sealed record ContextSnapshotHistoryResult(
    Guid SnapshotId,
    int SnapshotVersion,
    string Summary,
    decimal OverallConfidence,
    DateTime GeneratedAtUtc,
    bool IsStale,
    int FactCount);

public sealed record ContextSnapshotResult(
    Guid SnapshotId,
    Guid TenantId,
    string TenantSlug,
    Guid UserProfileId,
    string ExternalUserId,
    string FullName,
    string CompanyName,
    int SnapshotVersion,
    string Summary,
    decimal OverallConfidence,
    DateTime GeneratedAtUtc,
    bool IsStale,
    IReadOnlyList<ContextFactResult> Facts);

public sealed record ContextProfileResult(
    Guid SnapshotId,
    string TenantSlug,
    string ExternalUserId,
    string FullName,
    string CompanyName,
    string Summary,
    decimal OverallConfidence,
    DateTime GeneratedAtUtc,
    bool IsStale,
    OperationalSourceSummaryResult? SourceSummary,
    IReadOnlyList<ContextSnapshotHistoryResult> History,
    IReadOnlyList<ContextFactResult> Facts);

public sealed record AccountContextResult(
    string TenantSlug,
    string ExternalAccountId,
    string AccountName,
    string Domain,
    string Industry,
    string Segment,
    string Region,
    string LifecycleStage,
    IReadOnlyList<AccountContextUserResult> Users);

public sealed record AccountContextUserResult(
    string ExternalUserId,
    string FullName,
    string Email,
    string JobTitle,
    Guid? LatestSnapshotId,
    string? Summary,
    decimal? OverallConfidence,
    DateTime? GeneratedAtUtc,
    bool IsStale);

public sealed record GroundedContextFactResult(
    string CitationId,
    Guid FactId,
    string AttributeKey,
    string DisplayName,
    string ValueJson,
    FactValueType ValueType,
    decimal Confidence,
    DateTime ObservedAtUtc,
    DateTime? FreshUntilUtc,
    bool IsFresh,
    bool IsLowConfidence,
    string Explanation,
    string ProvenanceJson);

public sealed record SalesContextPackageResult(
    Guid SnapshotId,
    string TenantSlug,
    string ExternalUserId,
    string FullName,
    string CompanyName,
    string JobTitle,
    string Segment,
    string SalesObjective,
    string Summary,
    decimal OverallConfidence,
    DateTime GeneratedAtUtc,
    bool IsStale,
    bool HumanReviewRecommended,
    IReadOnlyList<string> MissingInformation,
    IReadOnlyList<string> WeakSignalMessages,
    IReadOnlyList<GroundedContextFactResult> Facts,
    string ContextPackageJson);

public sealed record QueueRecomputeResult(string CorrelationId, Guid TenantId, Guid UserProfileId, int ExecutionCount);

public sealed record ScheduledRecomputeDispatchResult(int QueuedUserCount, int SkippedUserCount);

public sealed record UserProfileResult(
    Guid Id,
    Guid TenantId,
    string ExternalUserId,
    string FullName,
    string Email,
    string CompanyName,
    string JobTitle,
    string Segment,
    DateTime LastSeenAtUtc,
    bool IsEmailMasked);

public sealed record SelectorExecutionPreviewResult(
    string Mode,
    bool IsSuccess,
    string SelectorName,
    string RawSourceDataJson,
    string NormalizedSourceDataJson,
    IReadOnlyList<string> ValidationErrors,
    string? ValueJson,
    string? ValueType,
    decimal? Confidence,
    DateTime? ObservedAtUtc,
    DateTime? FreshUntilUtc,
    string? Explanation,
    string? ProvenanceJson,
    string PipelineTraceJson);

public sealed record SelectorValidationResult(
    bool IsValid,
    IReadOnlyList<string> ValidationErrors,
    string RawSourceDataJson,
    string NormalizedSourceDataJson,
    string PipelineTraceJson);

public sealed record AuditEvent(
    Guid Id,
    Guid? TenantId,
    string Actor,
    string Action,
    string EntityType,
    string EntityId,
    string CorrelationId,
    string MetadataJson,
    string? BeforeJson,
    string? AfterJson,
    DateTime CreatedAtUtc);

public sealed record ContextSnapshotSummary(
    Guid SnapshotId,
    int SnapshotVersion,
    string Summary,
    decimal OverallConfidence,
    DateTime GeneratedAtUtc,
    bool IsStale,
    int FactCount);
