namespace KynticAI.Scout.Sdk;

public sealed class ScoutClient : IScoutClient, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public ScoutClient(ScoutClientOptions options)
        : this(new HttpClient(), options, ownsHttpClient: true)
    {
    }

    public ScoutClient(HttpClient httpClient, ScoutClientOptions options)
        : this(httpClient, options, ownsHttpClient: false)
    {
    }

    private ScoutClient(HttpClient httpClient, ScoutClientOptions options, bool ownsHttpClient)
    {
        this.httpClient = httpClient;
        this.ownsHttpClient = ownsHttpClient;
        var pipeline = new ScoutHttpPipeline(httpClient, options);
        Auth = new ScoutAuthClient(pipeline);
        Users = new ScoutUsersClient(pipeline);
        Accounts = new ScoutAccountsClient(pipeline);
        Snapshots = new ScoutSnapshotsClient(Users, Accounts, pipeline);
        Facts = new ScoutFactsClient(pipeline);
        Selectors = new ScoutSelectorsClient(pipeline);
        Recompute = new ScoutRecomputeClient(pipeline);
        Packages = new ScoutPackagesClient(pipeline);
        Audit = new ScoutAuditClient(pipeline);
        Events = new ScoutEventsClient(pipeline);
    }

    public IScoutAuthClient Auth { get; }

    public IScoutUsersClient Users { get; }

    public IScoutAccountsClient Accounts { get; }

    public IScoutSnapshotsClient Snapshots { get; }

    public IScoutFactsClient Facts { get; }

    public IScoutSelectorsClient Selectors { get; }

    public IScoutRecomputeClient Recompute { get; }

    public IScoutPackagesClient Packages { get; }

    public IScoutAuditClient Audit { get; }

    public IScoutEventsClient Events { get; }

    public IScoutTenantClient ForTenant(string tenantSlug)
        => new ScoutTenantClient(
            tenantSlug,
            Users,
            Accounts,
            Snapshots,
            Facts,
            Recompute,
            Packages,
            Audit,
            Events);

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }
}

internal sealed class ScoutAuthClient(ScoutHttpPipeline pipeline) : IScoutAuthClient
{
    public Task<AuthSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        => pipeline.SendAsync<AuthSession>(HttpMethod.Post, "/api/auth/login", request, cancellationToken);

    public Task<MachineTokenResponse> GetMachineTokenAsync(MachineTokenRequest request, CancellationToken cancellationToken = default)
        => pipeline.SendAsync<MachineTokenResponse>(HttpMethod.Post, "/api/auth/token", request, cancellationToken);

    public Task<AuthenticatedOperator> GetCurrentOperatorAsync(CancellationToken cancellationToken = default)
        => pipeline.SendAsync<AuthenticatedOperator>(HttpMethod.Get, "/api/auth/me", null, cancellationToken);
}

internal sealed class ScoutUsersClient(ScoutHttpPipeline pipeline) : IScoutUsersClient
{
    public Task<ContextProfileResult?> GetContextAsync(string tenantSlug, string externalUserId, CancellationToken cancellationToken = default)
        => pipeline.SendAsync<ContextProfileResult?>(
            HttpMethod.Get,
            $"/api/v1/context/users/{Uri.EscapeDataString(externalUserId)}?tenantSlug={Uri.EscapeDataString(tenantSlug)}",
            null,
            cancellationToken);
}

internal sealed class ScoutAccountsClient(ScoutHttpPipeline pipeline) : IScoutAccountsClient
{
    public Task<AccountContextResult?> GetContextAsync(string tenantSlug, string externalAccountId, CancellationToken cancellationToken = default)
        => pipeline.SendAsync<AccountContextResult?>(
            HttpMethod.Get,
            $"/api/v1/context/accounts/{Uri.EscapeDataString(externalAccountId)}?tenantSlug={Uri.EscapeDataString(tenantSlug)}",
            null,
            cancellationToken);
}

internal sealed class ScoutSnapshotsClient(
    IScoutUsersClient usersClient,
    IScoutAccountsClient accountsClient,
    ScoutHttpPipeline pipeline) : IScoutSnapshotsClient
{
    public Task<ContextSnapshotResult?> GetByIdAsync(string tenantSlug, Guid snapshotId, CancellationToken cancellationToken = default)
        => pipeline.SendAsync<ContextSnapshotResult?>(
            HttpMethod.Get,
            $"/api/v1/context/snapshots/{Uri.EscapeDataString(snapshotId.ToString())}?tenantSlug={Uri.EscapeDataString(tenantSlug)}",
            null,
            cancellationToken);

    public async Task<ContextSnapshotSummary?> GetLatestForUserAsync(string tenantSlug, string externalUserId, CancellationToken cancellationToken = default)
    {
        var context = await usersClient.GetContextAsync(tenantSlug, externalUserId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        return new ContextSnapshotSummary(
            context.SnapshotId,
            context.History.FirstOrDefault(x => x.SnapshotId == context.SnapshotId)?.SnapshotVersion ?? 0,
            context.Summary,
            context.OverallConfidence,
            context.GeneratedAtUtc,
            context.IsStale,
            context.Facts.Count);
    }

    public async Task<ContextSnapshotSummary?> GetLatestForAccountAsync(string tenantSlug, string externalAccountId, CancellationToken cancellationToken = default)
    {
        var context = await accountsClient.GetContextAsync(tenantSlug, externalAccountId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var latestUser = context.Users
            .Where(x => x.LatestSnapshotId.HasValue && x.GeneratedAtUtc.HasValue)
            .OrderByDescending(x => x.GeneratedAtUtc)
            .FirstOrDefault();
        if (latestUser?.LatestSnapshotId is null)
        {
            return null;
        }

        var snapshot = await GetByIdAsync(tenantSlug, latestUser.LatestSnapshotId.Value, cancellationToken);
        return snapshot is null
            ? new ContextSnapshotSummary(
                latestUser.LatestSnapshotId.Value,
                0,
                latestUser.Summary ?? string.Empty,
                latestUser.OverallConfidence ?? 0,
                latestUser.GeneratedAtUtc ?? DateTime.MinValue,
                latestUser.IsStale,
                0)
            : new ContextSnapshotSummary(
                snapshot.SnapshotId,
                snapshot.SnapshotVersion,
                snapshot.Summary,
                snapshot.OverallConfidence,
                snapshot.GeneratedAtUtc,
                snapshot.IsStale,
                snapshot.Facts.Count);
    }
}

internal sealed class ScoutFactsClient(ScoutHttpPipeline pipeline) : IScoutFactsClient
{
    public async Task<IReadOnlyList<ContextFactResult>> GetForUserAsync(
        string tenantSlug,
        string externalUserId,
        ContextFactLookupOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var page = await pipeline.SendAsync<PageResult<ContextFactResult>>(
            HttpMethod.Get,
            $"/api/v1/context/users/{Uri.EscapeDataString(externalUserId)}/facts{BuildFactQuery(tenantSlug, options)}",
            null,
            cancellationToken);
        return page.Items;
    }

    public async Task<IReadOnlyList<ContextFactResult>> GetForAccountAsync(
        string tenantSlug,
        string externalAccountId,
        ContextFactLookupOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var page = await pipeline.SendAsync<PageResult<ContextFactResult>>(
            HttpMethod.Get,
            $"/api/v1/context/accounts/{Uri.EscapeDataString(externalAccountId)}/facts{BuildFactQuery(tenantSlug, options)}",
            null,
            cancellationToken);
        return page.Items;
    }

    private static string BuildFactQuery(string tenantSlug, ContextFactLookupOptions? options)
    {
        var values = new List<string>
        {
            "tenantSlug=" + Uri.EscapeDataString(tenantSlug)
        };
        if (!string.IsNullOrWhiteSpace(options?.AttributeKey))
        {
            values.Add("attributeKey=" + Uri.EscapeDataString(options.AttributeKey));
        }
        if (options?.Page is not null)
        {
            values.Add("page=" + options.Page.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (options?.PageSize is not null)
        {
            values.Add("pageSize=" + options.PageSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return "?" + string.Join("&", values);
    }
}

internal sealed class ScoutSelectorsClient(ScoutHttpPipeline pipeline) : IScoutSelectorsClient
{
    public async Task<SelectorExecutionPreviewResult> PreviewAsync(PreviewSelectorInput input, CancellationToken cancellationToken = default)
        => await pipeline.SendGraphQlAsync<SelectorExecutionPreviewResult>(
            "PreviewSelector",
            """
            mutation PreviewSelector($input: PreviewSelectorInput!) {
              previewSelector(input: $input) {
                mode
                isSuccess
                selectorName
                rawSourceDataJson
                normalizedSourceDataJson
                validationErrors
                valueJson
                valueType
                confidence
                observedAtUtc
                freshUntilUtc
                explanation
                provenanceJson
                pipelineTraceJson
              }
            }
            """,
            new { input },
            "previewSelector",
            cancellationToken)
        ?? throw new ScoutException("Selector preview returned no result.");

    public async Task<SelectorValidationResult> ValidateAsync(ValidateSelectorInput input, CancellationToken cancellationToken = default)
        => await pipeline.SendGraphQlAsync<SelectorValidationResult>(
            "ValidateSelector",
            """
            mutation ValidateSelector($input: ValidateSelectorInput!) {
              validateSelector(input: $input) {
                isValid
                validationErrors
                rawSourceDataJson
                normalizedSourceDataJson
                pipelineTraceJson
              }
            }
            """,
            new { input },
            "validateSelector",
            cancellationToken)
        ?? throw new ScoutException("Selector validation returned no result.");
}

internal sealed class ScoutRecomputeClient(ScoutHttpPipeline pipeline) : IScoutRecomputeClient
{
    public async Task<QueueRecomputeResult> QueueForUserAsync(string tenantSlug, string externalUserId, string triggeredBy, CancellationToken cancellationToken = default)
        => await pipeline.SendGraphQlAsync<QueueRecomputeResult>(
            "QueueContextRecompute",
            """
            mutation QueueContextRecompute($input: QueueContextRecomputeInput!) {
              queueContextRecompute(input: $input) {
                correlationId
                tenantId
                userProfileId
                executionCount
              }
            }
            """,
            new { input = new QueueContextRecomputeInput(tenantSlug, externalUserId, triggeredBy) },
            "queueContextRecompute",
            cancellationToken)
        ?? throw new ScoutException("Recompute request returned no result.");
}

internal sealed class ScoutPackagesClient(ScoutHttpPipeline pipeline) : IScoutPackagesClient
{
    public Task<SalesContextPackageResult?> GetAiContextForUserAsync(string tenantSlug, string externalUserId, string salesObjective, CancellationToken cancellationToken = default)
        => pipeline.SendAsync<SalesContextPackageResult?>(
            HttpMethod.Post,
            $"/api/v1/context/users/{Uri.EscapeDataString(externalUserId)}/ai-safe-context-package?tenantSlug={Uri.EscapeDataString(tenantSlug)}",
            new { objective = salesObjective },
            cancellationToken);
}

internal sealed class ScoutAuditClient(ScoutHttpPipeline pipeline) : IScoutAuditClient
{
    private const int AuditPageSize = 200;

    public async Task<IReadOnlyList<AuditEvent>> GetEventsAsync(string tenantSlug, CancellationToken cancellationToken = default)
    {
        var events = new List<AuditEvent>();
        for (var page = 1; ; page++)
        {
            var response = await pipeline.SendAsync<V1PagedAuditResponse>(
                HttpMethod.Get,
                $"/api/v1/audit-events?tenantSlug={Uri.EscapeDataString(tenantSlug)}&page={page}&pageSize={AuditPageSize}",
                body: null,
                cancellationToken);

            events.AddRange(response.Items);

            if (!response.HasMore)
            {
                return events;
            }
        }
    }
}

internal sealed class ScoutEventsClient(ScoutHttpPipeline pipeline) : IScoutEventsClient
{
    public Task<SourceSystemEventAcceptedResult> IngestSourceSystemEventAsync(
        string tenantSlug,
        SourceSystemEventRequest request,
        CancellationToken cancellationToken = default)
        => pipeline.SendAsync<SourceSystemEventAcceptedResult>(
            HttpMethod.Post,
            $"/api/v1/events/source-system?tenantSlug={Uri.EscapeDataString(tenantSlug)}",
            request,
            cancellationToken);

    public Task<SourceSystemEventAcceptedResult> IngestConnectorSourceSystemEventAsync(
        string tenantSlug,
        Guid dataSourceId,
        SourceSystemEventRequest request,
        CancellationToken cancellationToken = default)
        => pipeline.SendAsync<SourceSystemEventAcceptedResult>(
            HttpMethod.Post,
            $"/api/v1/connectors/{Uri.EscapeDataString(dataSourceId.ToString())}/events/source-system?tenantSlug={Uri.EscapeDataString(tenantSlug)}",
            request,
            cancellationToken);
}

internal sealed class ScoutTenantClient : IScoutTenantClient
{
    public ScoutTenantClient(
        string tenantSlug,
        IScoutUsersClient usersClient,
        IScoutAccountsClient accountsClient,
        IScoutSnapshotsClient snapshotsClient,
        IScoutFactsClient factsClient,
        IScoutRecomputeClient recomputeClient,
        IScoutPackagesClient packagesClient,
        IScoutAuditClient auditClient,
        IScoutEventsClient eventsClient)
    {
        TenantSlug = tenantSlug;
        Users = new ScopedUsersClient(tenantSlug, usersClient);
        Accounts = new ScopedAccountsClient(tenantSlug, accountsClient);
        Snapshots = new ScopedSnapshotsClient(tenantSlug, snapshotsClient);
        Facts = new ScopedFactsClient(tenantSlug, factsClient);
        Recompute = new ScopedRecomputeClient(tenantSlug, recomputeClient);
        Packages = new ScopedPackagesClient(tenantSlug, packagesClient);
        Audit = new ScopedAuditClient(tenantSlug, auditClient);
        Events = new ScopedEventsClient(tenantSlug, eventsClient);
    }

    public string TenantSlug { get; }

    public IScopedUsersClient Users { get; }

    public IScopedAccountsClient Accounts { get; }

    public IScopedSnapshotsClient Snapshots { get; }

    public IScopedFactsClient Facts { get; }

    public IScopedRecomputeClient Recompute { get; }

    public IScopedPackagesClient Packages { get; }

    public IScopedAuditClient Audit { get; }

    public IScopedEventsClient Events { get; }
}

internal sealed class ScopedUsersClient(string tenantSlug, IScoutUsersClient inner) : IScopedUsersClient
{
    public Task<ContextProfileResult?> GetContextAsync(string externalUserId, CancellationToken cancellationToken = default)
        => inner.GetContextAsync(tenantSlug, externalUserId, cancellationToken);
}

internal sealed class ScopedAccountsClient(string tenantSlug, IScoutAccountsClient inner) : IScopedAccountsClient
{
    public Task<AccountContextResult?> GetContextAsync(string externalAccountId, CancellationToken cancellationToken = default)
        => inner.GetContextAsync(tenantSlug, externalAccountId, cancellationToken);
}

internal sealed class ScopedSnapshotsClient(string tenantSlug, IScoutSnapshotsClient inner) : IScopedSnapshotsClient
{
    public Task<ContextSnapshotResult?> GetByIdAsync(Guid snapshotId, CancellationToken cancellationToken = default)
        => inner.GetByIdAsync(tenantSlug, snapshotId, cancellationToken);

    public Task<ContextSnapshotSummary?> GetLatestForUserAsync(string externalUserId, CancellationToken cancellationToken = default)
        => inner.GetLatestForUserAsync(tenantSlug, externalUserId, cancellationToken);

    public Task<ContextSnapshotSummary?> GetLatestForAccountAsync(string externalAccountId, CancellationToken cancellationToken = default)
        => inner.GetLatestForAccountAsync(tenantSlug, externalAccountId, cancellationToken);
}

internal sealed class ScopedFactsClient(string tenantSlug, IScoutFactsClient inner) : IScopedFactsClient
{
    public Task<IReadOnlyList<ContextFactResult>> GetForUserAsync(string externalUserId, ContextFactLookupOptions? options = null, CancellationToken cancellationToken = default)
        => inner.GetForUserAsync(tenantSlug, externalUserId, options, cancellationToken);

    public Task<IReadOnlyList<ContextFactResult>> GetForAccountAsync(string externalAccountId, ContextFactLookupOptions? options = null, CancellationToken cancellationToken = default)
        => inner.GetForAccountAsync(tenantSlug, externalAccountId, options, cancellationToken);
}

internal sealed class ScopedRecomputeClient(string tenantSlug, IScoutRecomputeClient inner) : IScopedRecomputeClient
{
    public Task<QueueRecomputeResult> QueueForUserAsync(string externalUserId, string triggeredBy, CancellationToken cancellationToken = default)
        => inner.QueueForUserAsync(tenantSlug, externalUserId, triggeredBy, cancellationToken);
}

internal sealed class ScopedPackagesClient(string tenantSlug, IScoutPackagesClient inner) : IScopedPackagesClient
{
    public Task<SalesContextPackageResult?> GetAiContextForUserAsync(string externalUserId, string salesObjective, CancellationToken cancellationToken = default)
        => inner.GetAiContextForUserAsync(tenantSlug, externalUserId, salesObjective, cancellationToken);
}

internal sealed class ScopedAuditClient(string tenantSlug, IScoutAuditClient inner) : IScopedAuditClient
{
    public Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default)
        => inner.GetEventsAsync(tenantSlug, cancellationToken);
}

internal sealed class ScopedEventsClient(string tenantSlug, IScoutEventsClient inner) : IScopedEventsClient
{
    public Task<SourceSystemEventAcceptedResult> IngestSourceSystemEventAsync(SourceSystemEventRequest request, CancellationToken cancellationToken = default)
        => inner.IngestSourceSystemEventAsync(tenantSlug, request, cancellationToken);

    public Task<SourceSystemEventAcceptedResult> IngestConnectorSourceSystemEventAsync(Guid dataSourceId, SourceSystemEventRequest request, CancellationToken cancellationToken = default)
        => inner.IngestConnectorSourceSystemEventAsync(tenantSlug, dataSourceId, request, cancellationToken);
}
