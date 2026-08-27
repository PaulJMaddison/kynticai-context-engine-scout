using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Text;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Domain.Saas;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.Application.Services;

public sealed class ScoutService(
    IScoutDbContext dbContext,
    IOperationalReferenceDataProvider operationalReferenceDataProvider,
    IClock clock,
    IContextRecomputeQueue recomputeQueue,
    IValidator<UpsertDataSourceInput> upsertDataSourceValidator,
    IValidator<RegisterConnectorInput> registerConnectorValidator,
    IValidator<ValidateConnectorConfigurationInput> validateConnectorConfigurationValidator,
    IValidator<CheckConnectorHealthInput> checkConnectorHealthValidator,
    IValidator<UpsertSemanticAttributeInput> upsertSemanticAttributeValidator,
    IValidator<UpsertSelectorDefinitionInput> upsertSelectorValidator,
    IValidator<PublishSelectorDefinitionInput> publishSelectorValidator,
    IValidator<QueueContextRecomputeInput> queueContextRecomputeValidator,
    IValidator<UserContextLookupInput> userContextLookupValidator,
    IValidator<SalesContextPackageInput> salesContextPackageValidator,
    IValidator<PreviewSelectorInput> previewSelectorValidator,
    IValidator<ValidateSelectorInput> validateSelectorValidator,
    IValidator<RunScheduledRecomputeInput> runScheduledRecomputeValidator,
    IValidator<SourceSystemEventInput> sourceSystemEventValidator,
    IValidator<UpsertPromptTemplateInput> upsertPromptTemplateValidator,
    ISalesContextPackageService salesSupportAgentService,
    ICurrentActorService currentActorService,
    IConnectorRegistry connectorRegistry,
    IConnectorCredentialStore credentialStore,
    ISelectorExecutionEngine selectorExecutionEngine,
    IScheduledRecomputeDispatcher scheduledRecomputeDispatcher,
    IBillingEnforcementService billingEnforcementService,
    IUsageMeteringService usageMeteringService,
    IPlatformRuntimeOptions platformRuntimeOptions)
    : IScoutService
{
    public async Task<IReadOnlyList<Tenant>> GetTenantsAsync(CancellationToken cancellationToken)
    {
        var actor = currentActorService.GetCurrentActor();
        var query = dbContext.Tenants.AsNoTracking();
        if (!actor.IsSystem)
        {
            var normalizedSlug = NormalizeSlug(actor.TenantSlug);
            query = query.Where(x => x.Slug == normalizedSlug);
        }

        return await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DataSource>> GetDataSourcesAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        return await dbContext.DataSources
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ConnectorPluginDefinitionResult>> GetConnectorPluginsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ConnectorPluginDefinitionResult> results = connectorRegistry.GetPlugins()
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(plugin => new ConnectorPluginDefinitionResult(
                plugin.ConnectorType,
                plugin.DisplayName,
                plugin.Description,
                plugin.Aliases,
                plugin.SupportedDataSourceKinds.Select(static kind => kind.ToString()).ToList(),
                plugin.SupportedCapabilities.Select(static capability => capability.ToString()).ToList(),
                plugin.GetConfigurationSchema().ToJsonString(),
                plugin.GetCredentialSchema().ToJsonString(),
                plugin.GetSampleConfiguration().ToJsonString()))
            .ToList();
        return Task.FromResult(results);
    }

    public async Task<IReadOnlyList<ConnectorCatalogueEntryResult>> GetConnectorCatalogueAsync(CancellationToken cancellationToken)
    {
        var entries = await dbContext.ConnectorCatalogueEntries
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        return entries
            .Select(static entry => new ConnectorCatalogueEntryResult(
                entry.ConnectorType,
                entry.DisplayName,
                entry.Description,
                entry.Category,
                GetConnectorPublicStatus(entry),
                entry.Availability.ToString(),
                entry.Availability == ConnectorCatalogueAvailability.OpenCore,
                entry.Availability is ConnectorCatalogueAvailability.Enterprise or ConnectorCatalogueAvailability.SaaSManaged,
                entry.IsPlaceholder,
                entry.IsEnabled,
                DeserializeStringList(entry.SupportedDataSourceKindsJson),
                DeserializeStringList(entry.CapabilitiesJson),
                entry.ConfigurationSchemaJson,
                entry.CredentialSchemaJson,
                entry.HealthCheckMode))
            .ToList();
    }

    private static string GetConnectorPublicStatus(ConnectorCatalogueEntry entry)
    {
        if (entry.Availability == ConnectorCatalogueAvailability.OpenCore)
        {
            return "PublicGenericExample";
        }

        if (entry.Availability == ConnectorCatalogueAvailability.ComingSoon)
        {
            return "PlannedConnector";
        }

        if (entry.ConnectorType.Contains("customer", StringComparison.OrdinalIgnoreCase)
            || entry.ConnectorType.Contains("legacy-dotnet", StringComparison.OrdinalIgnoreCase)
            || entry.ConnectorType.Contains("billing-system", StringComparison.OrdinalIgnoreCase))
        {
            return "CustomerSpecificConnector";
        }

        return "PaidEnterpriseImplementation";
    }

    public async Task<IReadOnlyList<SemanticAttributeDefinition>> GetSemanticAttributesAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        return await dbContext.SemanticAttributeDefinitions
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderBy(x => x.Key)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SelectorDefinition>> GetSelectorsAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        return await dbContext.SelectorDefinitions
            .AsNoTracking()
            .Include(x => x.DataSource)
            .Include(x => x.TargetAttributeDefinition)
            .Where(x => x.TenantId == tenant.Id)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SelectorExecution>> GetSelectorExecutionsAsync(
        string tenantSlug,
        string? externalUserId,
        CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        var query = dbContext.SelectorExecutions
            .AsNoTracking()
            .Include(x => x.SelectorDefinition)
            .Include(x => x.UserProfile)
            .Where(x => x.TenantId == tenant.Id);

        if (!string.IsNullOrWhiteSpace(externalUserId))
        {
            var normalizedExternalUserId = externalUserId.Trim();
            query = query.Where(x => x.UserProfile.ExternalUserId == normalizedExternalUserId);
        }

        return await query.OrderByDescending(x => x.RequestedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PromptTemplate>> GetPromptTemplatesAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        return await dbContext.PromptTemplates
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentRun>> GetAgentRunsAsync(string tenantSlug, string? externalUserId, CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        var query = dbContext.AgentRuns
            .AsNoTracking()
            .Include(x => x.UserProfile)
            .Include(x => x.ContextSnapshot)
            .Where(x => x.TenantId == tenant.Id);

        if (!string.IsNullOrWhiteSpace(externalUserId))
        {
            var normalizedExternalUserId = externalUserId.Trim();
            query = query.Where(x => x.UserProfile.ExternalUserId == normalizedExternalUserId);
        }

        return await query.OrderByDescending(x => x.RequestedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEvent>> GetAuditEventsAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        return await dbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    public async Task<SaasArchitectureOverviewResult> GetSaasArchitectureOverviewAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        var subscription = await dbContext.TenantSubscriptions
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var workspaces = await dbContext.Workspaces
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var workspaceIds = workspaces.Select(x => x.Id).ToList();
        var memberCounts = await dbContext.WorkspaceMembers
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && workspaceIds.Contains(x.WorkspaceId))
            .GroupBy(x => x.WorkspaceId)
            .Select(x => new { WorkspaceId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.Count, cancellationToken);
        var connectorCounts = await dbContext.ConnectorInstallations
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && workspaceIds.Contains(x.WorkspaceId))
            .GroupBy(x => x.WorkspaceId)
            .Select(x => new { WorkspaceId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.Count, cancellationToken);
        var onboardingCounts = await dbContext.OnboardingStates
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && workspaceIds.Contains(x.WorkspaceId))
            .GroupBy(x => x.WorkspaceId)
            .Select(x => new
            {
                WorkspaceId = x.Key,
                Total = x.Count(),
                Completed = x.Count(item => item.Status == OnboardingStepStatus.Completed)
            })
            .ToDictionaryAsync(x => x.WorkspaceId, x => new { x.Total, x.Completed }, cancellationToken);
        var apiClients = await dbContext.ApiClients
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        var rawUsageRows = await dbContext.BillingUsageRecords
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .ToListAsync(cancellationToken);
        var usageRows = rawUsageRows
            .GroupBy(x => new { x.Metric, x.WindowStartUtc, x.WindowEndUtc })
            .Select(x => new SaasUsageSummaryResult(
                x.Key.Metric.ToString(),
                x.Sum(item => item.Quantity),
                x.Key.WindowStartUtc,
                x.Key.WindowEndUtc))
            .OrderByDescending(x => x.WindowStartUtc)
            .ThenBy(x => x.Metric)
            .Take(20)
            .ToList();

        return new SaasArchitectureOverviewResult(
            tenant.Id,
            tenant.Slug,
            tenant.Name,
            platformRuntimeOptions.Mode,
            platformRuntimeOptions.EnabledFeatureFlags,
            subscription is null
                ? null
                : new SaasSubscriptionSummaryResult(
                    subscription.Plan.ToString(),
                    subscription.Status.ToString(),
                    subscription.BillingCustomerReference,
                    subscription.StartedAtUtc,
                    subscription.TrialEndsAtUtc,
                    subscription.CurrentPeriodEndsAtUtc,
                    subscription.EntitlementsJson),
            workspaces.Select(workspace =>
            {
                onboardingCounts.TryGetValue(workspace.Id, out var onboarding);
                return new SaasWorkspaceSummaryResult(
                    workspace.Id,
                    workspace.Slug,
                    workspace.Name,
                    workspace.Status.ToString(),
                    workspace.IsDefault,
                    memberCounts.GetValueOrDefault(workspace.Id),
                    connectorCounts.GetValueOrDefault(workspace.Id),
                    onboarding?.Completed ?? 0,
                    onboarding?.Total ?? 0);
            }).ToList(),
            apiClients.Select(client => new SaasApiClientSummaryResult(
                client.Id,
                client.WorkspaceId,
                client.ClientId,
                client.DisplayName,
                client.Status.ToString(),
                DeserializeStringList(client.ScopesJson),
                client.LastUsedAtUtc)).ToList(),
            usageRows);
    }

    public async Task<OrganisationSettingsResult> GetOrganisationSettingsAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        var subscription = await dbContext.TenantSubscriptions
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var workspaceCount = await dbContext.Workspaces.CountAsync(x => x.TenantId == tenant.Id, cancellationToken);
        var userCount = await dbContext.OperatorAccounts.CountAsync(x => x.TenantId == tenant.Id, cancellationToken);
        var apiClientCount = await dbContext.ApiClients.CountAsync(x => x.TenantId == tenant.Id && x.Status == ApiClientStatus.Active, cancellationToken);

        return new OrganisationSettingsResult(
            tenant.Id,
            tenant.Slug,
            tenant.Name,
            tenant.IsActive,
            tenant.CreatedAtUtc,
            tenant.UpdatedAtUtc,
            subscription?.Plan.ToString(),
            subscription?.Status.ToString(),
            workspaceCount,
            userCount,
            apiClientCount);
    }

    public async Task<IReadOnlyList<WorkspaceSummaryResult>> GetWorkspacesAsync(
        string tenantSlug,
        string? status,
        CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        var query = dbContext.Workspaces
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id);

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<WorkspaceStatus>(status.Trim(), ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }

        return await query
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new WorkspaceSummaryResult(
                x.Id,
                x.Slug,
                x.Name,
                x.Description,
                x.Status.ToString(),
                x.IsDefault))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OperatorAccountSummaryResult>> GetOperatorAccountsAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        var accounts = await dbContext.OperatorAccounts
            .AsNoTracking()
            .Include(x => x.WorkspaceMemberships)
                .ThenInclude(x => x.Workspace)
            .Where(x => x.TenantId == tenant.Id)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        return accounts.Select(MapOperatorAccount).ToList();
    }

    public async Task<OperatorAccountSummaryResult> UpdateOperatorAccountAsync(UpdateOperatorAccountInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.DisplayName))
        {
            throw new ValidationException([new ValidationFailure(nameof(input.DisplayName), "Display name is required.")]);
        }

        if (!Enum.TryParse<OperatorRole>(NormalizeRoleName(input.Role), ignoreCase: true, out var role)
            || role == OperatorRole.ApiClient)
        {
            throw new ValidationException([new ValidationFailure(nameof(input.Role), "Role must be PlatformOwner, TenantAdmin, IntegrationAdmin, Analyst, SalesUser, or ReadOnly.")]);
        }

        var tenant = await GetTenantAsync(input.TenantSlug, cancellationToken);
        var actor = currentActorService.GetCurrentActor();
        if (!actor.IsSystem && actor.Role is not (OperatorRole.PlatformOwner or OperatorRole.TenantAdmin))
        {
            await WriteAdminPermissionDeniedAsync(tenant.Id, actor.Email, "admin.user.update.denied", input.OperatorAccountId, "insufficient-role", cancellationToken);
            throw new UnauthorizedAccessException("Only platform owners and tenant admins can update user roles.");
        }

        if (!actor.IsSystem && actor.Role != OperatorRole.PlatformOwner && role == OperatorRole.PlatformOwner)
        {
            await WriteAdminPermissionDeniedAsync(tenant.Id, actor.Email, "admin.user.update.denied", input.OperatorAccountId, "platform-owner-escalation", cancellationToken);
            throw new UnauthorizedAccessException("Only platform owners can grant the platform owner role.");
        }

        var account = await dbContext.OperatorAccounts
            .Include(x => x.WorkspaceMemberships)
                .ThenInclude(x => x.Workspace)
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.Id == input.OperatorAccountId, cancellationToken)
            ?? throw new InvalidOperationException($"Operator account '{input.OperatorAccountId}' was not found.");
        var before = Serialize(account);
        account.UpdateProfile(input.DisplayName, role, clock.UtcNow);
        account.SetActive(input.IsActive, clock.UtcNow);

        dbContext.AuditEvents.Add(CreateAuditEvent(
            tenant.Id,
            actor.Email,
            "admin.user.updated",
            nameof(OperatorAccount),
            account.Id,
            before,
            Serialize(account),
            clock.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapOperatorAccount(account);
    }

    public async Task<IReadOnlyList<UserProfileResult>> GetUserProfilesAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        var actor = currentActorService.GetCurrentActor();
        var users = await dbContext.UserProfiles
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderBy(x => x.ExternalUserId)
            .ToListAsync(cancellationToken);

        return users
            .Select(user => new UserProfileResult(
                user.Id,
                user.TenantId,
                user.ExternalUserId,
                user.FullName,
                actor.CanViewSensitivePii ? user.Email : MaskEmail(user.Email),
                user.CompanyName,
                user.JobTitle,
                user.Segment,
                user.LastSeenAtUtc,
                !actor.CanViewSensitivePii))
            .ToList();
    }

    public async Task<IReadOnlyList<BlueprintImportHistoryResult>> GetBlueprintImportsAsync(string tenantSlug, string? status, CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        var query = dbContext.BlueprintImports
            .AsNoTracking()
            .Include(x => x.Workspace)
            .Where(x => x.TenantId == tenant.Id);

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<BlueprintImportStatus>(status.Trim(), ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }

        var imports = await query
            .OrderByDescending(x => x.UploadedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        return imports.Select(import => new BlueprintImportHistoryResult(
            import.Id,
            import.TenantId,
            import.WorkspaceId,
            import.Workspace?.Slug,
            import.Name,
            import.Status.ToString(),
            import.UploadedBy,
            CountJsonArray(import.ValidationIssuesJson),
            CountJsonArray(import.PreviewJson),
            import.ImportSummaryJson,
            import.UploadedAtUtc,
            import.ValidatedAtUtc,
            import.ImportedAtUtc)).ToList();
    }

    public async Task<IReadOnlyList<GovernancePolicyResult>> GetGovernancePoliciesAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        var piiRules = await dbContext.PiiRules
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderBy(x => x.Key)
            .ToListAsync(cancellationToken);
        var auditPolicies = await dbContext.AuditPolicies
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderBy(x => x.Key)
            .ToListAsync(cancellationToken);

        return piiRules
            .Select(rule => new GovernancePolicyResult(
                rule.Id,
                rule.TenantId,
                rule.BlueprintImportId,
                "PII rule",
                rule.Key,
                rule.DisplayName,
                rule.Description,
                rule.Status.ToString(),
                rule.RuleJson,
                rule.CreatedAtUtc,
                rule.UpdatedAtUtc))
            .Concat(auditPolicies.Select(policy => new GovernancePolicyResult(
                policy.Id,
                policy.TenantId,
                policy.BlueprintImportId,
                "Audit policy",
                policy.Key,
                policy.DisplayName,
                policy.Description,
                policy.Status.ToString(),
                policy.PolicyJson,
                policy.CreatedAtUtc,
                policy.UpdatedAtUtc)))
            .OrderBy(x => x.PolicyType)
            .ThenBy(x => x.Key)
            .ToList();
    }

    public async Task<AuditEventExportResult> ExportAuditEventsAsync(string tenantSlug, string format, CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        var actor = currentActorService.GetCurrentActor();
        var events = await dbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(1000)
            .ToListAsync(cancellationToken);
        var normalizedFormat = string.IsNullOrWhiteSpace(format) ? "json" : format.Trim().ToLowerInvariant();
        var utcNow = clock.UtcNow;

        dbContext.AuditEvents.Add(AuditEvent.Create(
            tenant.Id,
            actor.Email,
            "admin.audit_export.created",
            nameof(AuditEvent),
            tenant.Id.ToString("D"),
            Guid.NewGuid().ToString("N"),
            JsonSerializer.Serialize(new { tenantSlug = tenant.Slug, format = normalizedFormat, rowCount = events.Count }),
            null,
            null,
            utcNow));
        await dbContext.SaveChangesAsync(cancellationToken);

        return normalizedFormat switch
        {
            "csv" => new AuditEventExportResult(
                $"scout-audit-{tenant.Slug}-{utcNow:yyyyMMddHHmmss}.csv",
                "text/csv",
                BuildAuditCsv(events)),
            "json" => new AuditEventExportResult(
                $"scout-audit-{tenant.Slug}-{utcNow:yyyyMMddHHmmss}.json",
                "application/json",
                JsonSerializer.Serialize(events, AuditSerializerOptions)),
            _ => throw new ValidationException([new ValidationFailure(nameof(format), "Format must be json or csv.")])
        };
    }

    public async Task<DataSource> UpsertDataSourceAsync(UpsertDataSourceInput input, CancellationToken cancellationToken)
    {
        await upsertDataSourceValidator.ValidateAndThrowAsync(input, cancellationToken);
        var tenant = await GetTenantAsync(input.TenantSlug, cancellationToken);
        var utcNow = clock.UtcNow;

        DataSource entity;
        if (input.Id is { } id)
        {
            entity = await dbContext.DataSources.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, cancellationToken)
                ?? throw new InvalidOperationException($"Data source '{id}' was not found.");
            var before = Serialize(entity);
            entity.Update(input.Name, input.Description, input.Kind, input.ConnectionConfigJson, utcNow);
            dbContext.AuditEvents.Add(CreateAuditEvent(tenant.Id, "system", "data-source.updated", nameof(DataSource), entity.Id, before, Serialize(entity), utcNow));
        }
        else
        {
            entity = DataSource.Create(tenant.Id, input.Name, input.Description, input.Kind, input.ConnectionConfigJson, utcNow);
            dbContext.DataSources.Add(entity);
            dbContext.AuditEvents.Add(CreateAuditEvent(tenant.Id, "system", "data-source.created", nameof(DataSource), entity.Id, null, Serialize(entity), utcNow));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<ConnectorRegistrationResult> RegisterConnectorAsync(RegisterConnectorInput input, CancellationToken cancellationToken)
    {
        await registerConnectorValidator.ValidateAndThrowAsync(input, cancellationToken);
        var tenant = await GetTenantAsync(input.TenantSlug, cancellationToken);
        var plugin = connectorRegistry.GetRequiredPlugin(input.ConnectorType);
        var configuration = ParseJsonObject(input.ConfigurationJson, "ConfigurationJson");
        configuration["connectorType"] = plugin.ConnectorType;
        var credentials = ParseJsonObject(input.CredentialsJson, "CredentialsJson", allowEmpty: true);

        await EnsureValidConnectorConfigurationAsync(plugin, input.Kind, configuration, credentials, cancellationToken);

        var utcNow = clock.UtcNow;
        DataSource entity;
        if (input.Id is { } id)
        {
            entity = await dbContext.DataSources.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, cancellationToken)
                ?? throw new InvalidOperationException($"Data source '{id}' was not found.");
            if (credentials.Count == 0)
            {
                var existingConfiguration = ParseJsonObject(entity.ConnectionConfigJson, "ConnectionConfigJson", allowEmpty: true);
                if (existingConfiguration["credentials"] is JsonObject existingCredentials)
                {
                    configuration["credentials"] = existingCredentials.DeepClone();
                }
            }

            var before = Serialize(entity);
            entity.Update(input.Name, input.Description, input.Kind, configuration.ToJsonString(), utcNow);
            dbContext.AuditEvents.Add(CreateAuditEvent(tenant.Id, "system", "connector.updated", nameof(DataSource), entity.Id, before, Serialize(entity), utcNow));
        }
        else
        {
            entity = DataSource.Create(tenant.Id, input.Name, input.Description, input.Kind, configuration.ToJsonString(), utcNow);
            dbContext.DataSources.Add(entity);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (credentials.Count > 0)
        {
            var refs = await credentialStore.PersistCredentialsAsync(tenant.Id, entity.Id, plugin.ConnectorType, credentials, cancellationToken);
            configuration["credentials"] = refs;
            entity.Update(input.Name, input.Description, input.Kind, configuration.ToJsonString(), utcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (input.Id is null)
        {
            dbContext.AuditEvents.Add(CreateAuditEvent(tenant.Id, "system", "connector.registered", nameof(DataSource), entity.Id, null, Serialize(entity), utcNow));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new ConnectorRegistrationResult(
            entity.Id,
            entity.Name,
            entity.Description,
            plugin.ConnectorType,
            SanitizeConfiguration(configuration).ToJsonString(),
            entity.Status.ToString());
    }

    public async Task<ConnectorConfigurationValidationResultModel> ValidateConnectorConfigurationAsync(
        ValidateConnectorConfigurationInput input,
        CancellationToken cancellationToken)
    {
        await validateConnectorConfigurationValidator.ValidateAndThrowAsync(input, cancellationToken);
        var plugin = connectorRegistry.GetRequiredPlugin(input.ConnectorType);
        var configuration = ParseJsonObject(input.ConfigurationJson, "ConfigurationJson");
        configuration["connectorType"] = plugin.ConnectorType;
        var credentials = ParseJsonObject(input.CredentialsJson, "CredentialsJson", allowEmpty: true);
        var validation = await plugin.ValidateConfigurationAsync(
            new ConnectorConfigurationValidationRequest(plugin.ConnectorType, input.Kind, configuration, credentials),
            cancellationToken);

        return new ConnectorConfigurationValidationResultModel(
            plugin.ConnectorType,
            validation.IsValid,
            validation.Errors,
            validation.SanitizedConfigurationJson,
            validation.SchemaJson);
    }

    public async Task<ConnectorHealthResult> CheckConnectorHealthAsync(CheckConnectorHealthInput input, CancellationToken cancellationToken)
    {
        await checkConnectorHealthValidator.ValidateAndThrowAsync(input, cancellationToken);
        var tenant = await GetTenantAsync(input.TenantSlug, cancellationToken);
        var dataSource = await dbContext.DataSources.FirstOrDefaultAsync(
            x => x.Id == input.DataSourceId && x.TenantId == tenant.Id,
            cancellationToken) ?? throw new InvalidOperationException($"Data source '{input.DataSourceId}' was not found.");
        var configuration = await credentialStore.ResolveConfigurationSecretsAsync(tenant.Id, ParseJsonObject(dataSource.ConnectionConfigJson, "ConnectionConfigJson"), cancellationToken);
        var connectorType = configuration["connectorType"]?.GetValue<string>() ?? throw new InvalidOperationException("Connector configuration is missing connectorType.");
        var plugin = connectorRegistry.GetRequiredPlugin(connectorType);
        UserProfile? user = null;
        if (!string.IsNullOrWhiteSpace(input.ExternalUserId))
        {
            user = await GetUserProfileAsync(tenant.Id, input.ExternalUserId, cancellationToken);
        }

        var health = await plugin.CheckHealthAsync(
            new ConnectorHealthCheckRequest(
                plugin.ConnectorType,
                dataSource.Kind,
                configuration,
                configuration["credentials"] as JsonObject ?? new JsonObject(),
                user is null ? null : new ConnectorSubject(user.ExternalUserId, user.FullName, user.Email, user.CompanyName, user.JobTitle, user.Segment),
                ParseConnectorRunMode(input.Mode)),
            cancellationToken);

        return new ConnectorHealthResult(
            dataSource.Id,
            plugin.ConnectorType,
            health.IsHealthy,
            health.Status,
            health.Messages,
            health.DetailsJson,
            health.CheckedAtUtc);
    }

    public async Task<SemanticAttributeDefinition> UpsertSemanticAttributeAsync(
        UpsertSemanticAttributeInput input,
        CancellationToken cancellationToken)
    {
        await upsertSemanticAttributeValidator.ValidateAndThrowAsync(input, cancellationToken);
        var tenant = await GetTenantAsync(input.TenantSlug, cancellationToken);
        var utcNow = clock.UtcNow;

        SemanticAttributeDefinition entity;
        if (input.Id is { } id)
        {
            entity = await dbContext.SemanticAttributeDefinitions
                .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, cancellationToken)
                ?? throw new InvalidOperationException($"Semantic attribute '{id}' was not found.");
            var before = Serialize(entity);
            entity.Update(input.DisplayName, input.Description, input.DataType, input.ExampleValueJson, utcNow);
            dbContext.AuditEvents.Add(CreateAuditEvent(tenant.Id, "system", "semantic-attribute.updated", nameof(SemanticAttributeDefinition), entity.Id, before, Serialize(entity), utcNow));
        }
        else
        {
            var keyExists = await dbContext.SemanticAttributeDefinitions.AnyAsync(
                x => x.TenantId == tenant.Id && x.Key == input.Key,
                cancellationToken);
            if (keyExists)
            {
                throw new InvalidOperationException($"Semantic attribute key '{input.Key}' already exists.");
            }

            entity = SemanticAttributeDefinition.Create(
                tenant.Id,
                input.Key,
                input.DisplayName,
                input.Description,
                input.DataType,
                input.ExampleValueJson,
                input.IsSystem,
                utcNow);
            dbContext.SemanticAttributeDefinitions.Add(entity);
            dbContext.AuditEvents.Add(CreateAuditEvent(tenant.Id, "system", "semantic-attribute.created", nameof(SemanticAttributeDefinition), entity.Id, null, Serialize(entity), utcNow));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<SelectorDefinition> UpsertSelectorAsync(UpsertSelectorDefinitionInput input, CancellationToken cancellationToken)
    {
        await upsertSelectorValidator.ValidateAndThrowAsync(input, cancellationToken);
        var tenant = await GetTenantAsync(input.TenantSlug, cancellationToken);
        var utcNow = clock.UtcNow;

        var targetAttribute = await dbContext.SemanticAttributeDefinitions.FirstOrDefaultAsync(
            x => x.Id == input.TargetAttributeDefinitionId && x.TenantId == tenant.Id,
            cancellationToken) ?? throw new InvalidOperationException("Target attribute does not exist in the tenant.");

        if (input.DataSourceId.HasValue)
        {
            var dataSourceExists = await dbContext.DataSources.AnyAsync(
                x => x.Id == input.DataSourceId && x.TenantId == tenant.Id,
                cancellationToken);
            if (!dataSourceExists)
            {
                throw new InvalidOperationException("Data source does not exist in the tenant.");
            }
        }

        SelectorDefinition entity;
        if (input.Id is { } id)
        {
            entity = await dbContext.SelectorDefinitions.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, cancellationToken)
                ?? throw new InvalidOperationException($"Selector '{id}' was not found.");
            var before = Serialize(entity);
            entity.Update(
                input.DataSourceId,
                targetAttribute.Id,
                input.Name,
                input.Description,
                input.MappingKind,
                input.ExpressionJson,
                input.ExplanationTemplate,
                input.ValidationSchemaJson,
                input.DefaultConfidence,
                input.FreshnessWindowMinutes,
                input.Priority,
                input.ScheduleIntervalMinutes,
                utcNow);
            dbContext.AuditEvents.Add(CreateAuditEvent(tenant.Id, "system", "selector.updated", nameof(SelectorDefinition), entity.Id, before, Serialize(entity), utcNow));
        }
        else
        {
            await billingEnforcementService.EnsureWithinLimitAsync(tenant.Id, BillingLimitMetric.Selectors, 1, cancellationToken);
            entity = SelectorDefinition.Create(
                tenant.Id,
                input.DataSourceId,
                targetAttribute.Id,
                input.Name,
                input.Description,
                input.MappingKind,
                input.ExpressionJson,
                input.ExplanationTemplate,
                input.ValidationSchemaJson,
                input.DefaultConfidence,
                input.FreshnessWindowMinutes,
                input.Priority,
                input.ScheduleIntervalMinutes,
                utcNow);
            dbContext.SelectorDefinitions.Add(entity);
            dbContext.AuditEvents.Add(CreateAuditEvent(tenant.Id, "system", "selector.created", nameof(SelectorDefinition), entity.Id, null, Serialize(entity), utcNow));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<SelectorDefinition> PublishSelectorAsync(PublishSelectorDefinitionInput input, CancellationToken cancellationToken)
    {
        await publishSelectorValidator.ValidateAndThrowAsync(input, cancellationToken);
        var tenant = await GetTenantAsync(input.TenantSlug, cancellationToken);
        var utcNow = clock.UtcNow;

        var selector = await dbContext.SelectorDefinitions
            .FirstOrDefaultAsync(x => x.Id == input.SelectorDefinitionId && x.TenantId == tenant.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Selector '{input.SelectorDefinitionId}' was not found.");

        var before = Serialize(selector);
        selector.Publish(utcNow);
        dbContext.AuditEvents.Add(CreateAuditEvent(tenant.Id, "system", "selector.published", nameof(SelectorDefinition), selector.Id, before, Serialize(selector), utcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
        return selector;
    }

    public async Task<PromptTemplate> UpsertPromptTemplateAsync(UpsertPromptTemplateInput input, CancellationToken cancellationToken)
    {
        await upsertPromptTemplateValidator.ValidateAndThrowAsync(input, cancellationToken);
        var tenant = await GetTenantAsync(input.TenantSlug, cancellationToken);
        var utcNow = clock.UtcNow;

        PromptTemplate entity;
        if (input.Id is { } id)
        {
            entity = await dbContext.PromptTemplates.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, cancellationToken)
                ?? throw new InvalidOperationException($"Prompt template '{id}' was not found.");
            var before = Serialize(entity);
            entity.Update(
                input.Name,
                input.Description,
                input.SystemPrompt,
                input.DeveloperPrompt,
                input.UserPromptTemplate,
                input.OutputSchemaJson,
                input.GuardrailsJson,
                utcNow);
            dbContext.AuditEvents.Add(CreateAuditEvent(tenant.Id, "system", "prompt-template.updated", nameof(PromptTemplate), entity.Id, before, Serialize(entity), utcNow));
        }
        else
        {
            entity = PromptTemplate.Create(
                tenant.Id,
                input.Name,
                input.Description,
                input.SystemPrompt,
                input.DeveloperPrompt,
                input.UserPromptTemplate,
                input.OutputSchemaJson,
                input.GuardrailsJson,
                utcNow);
            dbContext.PromptTemplates.Add(entity);
            dbContext.AuditEvents.Add(CreateAuditEvent(tenant.Id, "system", "prompt-template.created", nameof(PromptTemplate), entity.Id, null, Serialize(entity), utcNow));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<QueueRecomputeResult> QueueContextRecomputeAsync(QueueContextRecomputeInput input, CancellationToken cancellationToken)
    {
        await queueContextRecomputeValidator.ValidateAndThrowAsync(input, cancellationToken);
        var tenant = await GetTenantAsync(input.TenantSlug, cancellationToken);
        var user = await GetUserProfileAsync(tenant.Id, input.ExternalUserId, cancellationToken);
        await billingEnforcementService.EnsureWithinLimitAsync(tenant.Id, BillingLimitMetric.Recomputations, 1, cancellationToken);
        var selectors = await dbContext.SelectorDefinitions
            .Where(x => x.TenantId == tenant.Id && x.Status == SelectorStatus.Published)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        if (selectors.Count == 0)
        {
            throw new InvalidOperationException("No published selectors are available for the tenant.");
        }

        var utcNow = clock.UtcNow;
        var correlationId = Guid.NewGuid().ToString("N");
        var executions = selectors
            .Select(selector => SelectorExecution.Create(tenant.Id, selector.Id, user.Id, correlationId, input.TriggeredBy, SelectorExecutionMode.Live, utcNow))
            .ToList();
        var recomputeJob = RecomputeJob.Create(
            tenant.Id,
            user.Id,
            correlationId,
            input.TriggeredBy,
            executions.Count,
            $"Manual recompute requested for {user.ExternalUserId}.",
            JsonSerializer.Serialize(new
            {
                mode = "manual",
                user.ExternalUserId,
                selectorCount = executions.Count
            }),
            utcNow);

        dbContext.SelectorExecutions.AddRange(executions);
        dbContext.RecomputeJobs.Add(recomputeJob);
        dbContext.AuditEvents.Add(CreateAuditEvent(tenant.Id, input.TriggeredBy, "context.recompute.queued", nameof(UserProfile), user.Id, null, JsonSerializer.Serialize(new
        {
            user.ExternalUserId,
            correlationId,
            executionCount = executions.Count
        }), utcNow));
        await usageMeteringService.RecordAsync(
            new UsageRecordInput(
                tenant.Id,
                null,
                BillingUsageMetric.RecomputeRequested,
                1,
                "context-recompute",
                new { input.ExternalUserId, input.TriggeredBy, correlationId }),
            cancellationToken,
            saveImmediately: false);

        await dbContext.SaveChangesAsync(cancellationToken);
        await recomputeQueue.EnqueueAsync(new ContextRecomputeRequest(tenant.Id, user.Id, correlationId, executions.Select(x => x.Id).ToList()), cancellationToken);

        return new QueueRecomputeResult(correlationId, tenant.Id, user.Id, executions.Count);
    }

    public async Task<ContextProfileResult?> GetUserContextAsync(UserContextLookupInput input, CancellationToken cancellationToken)
    {
        await userContextLookupValidator.ValidateAndThrowAsync(input, cancellationToken);
        var tenant = await GetTenantAsync(input.TenantSlug, cancellationToken);
        var user = await dbContext.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.ExternalUserId == input.ExternalUserId.Trim(), cancellationToken);

        if (user is null)
        {
            return null;
        }

        var snapshot = await dbContext.ContextSnapshots
            .AsNoTracking()
            .Include(x => x.Facts)
            .Where(x => x.TenantId == tenant.Id && x.UserProfileId == user.Id)
            .OrderByDescending(x => x.GeneratedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot is null)
        {
            return null;
        }

        await billingEnforcementService.EnsureWithinLimitAsync(tenant.Id, BillingLimitMetric.ContextLookups, 1, cancellationToken);
        await WriteReadAuditAsync(
            tenant.Id,
            actor: currentActorService.GetCurrentActor(),
            action: "context-profile.read",
            entityType: nameof(UserProfile),
            entityId: user.Id,
            metadata: new
            {
                user.ExternalUserId,
                factCount = snapshot.Facts.Count,
                maskedFields = Array.Empty<string>()
            },
            cancellationToken);
        await usageMeteringService.RecordAsync(
            new UsageRecordInput(
                tenant.Id,
                null,
                BillingUsageMetric.ContextLookup,
                1,
                "context-user-lookup",
                new { user.ExternalUserId, snapshotId = snapshot.Id }),
            cancellationToken);

        var sourceSummary = await BuildOperationalSourceSummaryAsync(tenant.Slug, user.ExternalUserId, cancellationToken);
        var history = await dbContext.ContextSnapshots
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && x.UserProfileId == user.Id)
            .OrderByDescending(x => x.GeneratedAtUtc)
            .Take(6)
            .Select(x => new ContextSnapshotHistoryResult(
                x.Id,
                x.SnapshotVersion,
                x.Summary,
                x.OverallConfidence,
                x.GeneratedAtUtc,
                x.IsStale,
                x.Facts.Count))
            .ToListAsync(cancellationToken);

        var facts = snapshot.Facts
            .OrderBy(x => x.AttributeKey)
            .Select(x => new ContextFactResult(
                x.Id,
                x.AttributeKey,
                x.ValueJson,
                x.ValueType,
                x.Confidence,
                x.ObservedAtUtc,
                x.FreshUntilUtc,
                x.SourceSelectorDefinitionId,
                x.Explanation,
                x.ProvenanceJson))
            .ToList();

        return new ContextProfileResult(
            snapshot.Id,
            tenant.Slug,
            user.ExternalUserId,
            user.FullName,
            user.CompanyName,
            snapshot.Summary,
            snapshot.OverallConfidence,
            snapshot.GeneratedAtUtc,
            snapshot.IsStale || snapshot.Facts.Any(x => x.FreshUntilUtc.HasValue && x.FreshUntilUtc.Value < clock.UtcNow),
            sourceSummary,
            history,
            facts);
    }

    public async Task<AccountContextResult?> GetAccountContextAsync(
        string tenantSlug,
        string externalAccountId,
        CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        var account = await operationalReferenceDataProvider.GetAccountAsync(
            tenant.Slug,
            externalAccountId,
            cancellationToken);
        if (account is null)
        {
            return null;
        }

        var externalUserIds = account.Contacts
            .Select(x => x.ExternalUserId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var users = await dbContext.UserProfiles
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && externalUserIds.Contains(x.ExternalUserId))
            .ToListAsync(cancellationToken);
        var userIds = users.Select(x => x.Id).ToList();
        var snapshots = await dbContext.ContextSnapshots
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && userIds.Contains(x.UserProfileId))
            .GroupBy(x => x.UserProfileId)
            .Select(x => x.OrderByDescending(snapshot => snapshot.GeneratedAtUtc).First())
            .ToListAsync(cancellationToken);
        var snapshotsByUserId = snapshots.ToDictionary(x => x.UserProfileId);
        var profilesByExternalId = users.ToDictionary(x => x.ExternalUserId, StringComparer.Ordinal);
        var actor = currentActorService.GetCurrentActor();

        await billingEnforcementService.EnsureWithinLimitAsync(
            tenant.Id,
            BillingLimitMetric.ContextLookups,
            1,
            cancellationToken);
        await WriteReadAuditAsync(
            tenant.Id,
            actor,
            "account-context.read",
            nameof(OperationalAccountReferenceResult),
            // Reference/demo data has no durable Scout-owned entity id; the
            // external account id is recorded in the audit metadata below.
            Guid.Empty,
            new { account.ExternalAccountId, userCount = users.Count, referenceData = operationalReferenceDataProvider.IsEnabled },
            cancellationToken);
        await usageMeteringService.RecordAsync(
            new UsageRecordInput(
                tenant.Id,
                null,
                BillingUsageMetric.ContextLookup,
                1,
                "context-account-lookup",
                new { account.ExternalAccountId, userCount = users.Count }),
            cancellationToken);

        return new AccountContextResult(
            tenant.Slug,
            account.ExternalAccountId,
            account.AccountName,
            account.Domain,
            account.Industry,
            account.Segment,
            account.Region,
            account.LifecycleStage,
            account.Contacts.Select(contact =>
            {
                profilesByExternalId.TryGetValue(contact.ExternalUserId, out var profile);
                var snapshot = profile is null || !snapshotsByUserId.TryGetValue(profile.Id, out var foundSnapshot)
                    ? null
                    : foundSnapshot;
                return new AccountContextUserResult(
                    contact.ExternalUserId,
                    contact.FullName,
                    actor.CanViewSensitivePii ? contact.Email : MaskEmail(contact.Email),
                    contact.JobTitle,
                    snapshot?.Id,
                    snapshot?.Summary,
                    snapshot?.OverallConfidence,
                    snapshot?.GeneratedAtUtc,
                    snapshot?.IsStale ?? false);
            }).ToList());
    }

    public async Task<ContextSnapshotResult?> GetContextSnapshotAsync(
        string tenantSlug,
        Guid snapshotId,
        CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        var snapshot = await dbContext.ContextSnapshots
            .AsNoTracking()
            .Include(x => x.UserProfile)
            .Include(x => x.Facts)
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.Id == snapshotId, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        await billingEnforcementService.EnsureWithinLimitAsync(tenant.Id, BillingLimitMetric.ContextLookups, 1, cancellationToken);
        await WriteReadAuditAsync(
            tenant.Id,
            currentActorService.GetCurrentActor(),
            "context-snapshot.read",
            nameof(ContextSnapshot),
            snapshot.Id,
            new { snapshot.UserProfile.ExternalUserId, factCount = snapshot.Facts.Count },
            cancellationToken);
        await usageMeteringService.RecordAsync(
            new UsageRecordInput(
                tenant.Id,
                null,
                BillingUsageMetric.ContextLookup,
                1,
                "context-snapshot-lookup",
                new { snapshotId = snapshot.Id, snapshot.UserProfile.ExternalUserId }),
            cancellationToken);

        return new ContextSnapshotResult(
            snapshot.Id,
            tenant.Id,
            tenant.Slug,
            snapshot.UserProfileId,
            snapshot.UserProfile.ExternalUserId,
            snapshot.UserProfile.FullName,
            snapshot.UserProfile.CompanyName,
            snapshot.SnapshotVersion,
            snapshot.Summary,
            snapshot.OverallConfidence,
            snapshot.GeneratedAtUtc,
            snapshot.IsStale || snapshot.Facts.Any(x => x.FreshUntilUtc.HasValue && x.FreshUntilUtc.Value < clock.UtcNow),
            snapshot.Facts
                .OrderBy(x => x.AttributeKey)
                .Select(x => new ContextFactResult(
                    x.Id,
                    x.AttributeKey,
                    x.ValueJson,
                    x.ValueType,
                    x.Confidence,
                    x.ObservedAtUtc,
                    x.FreshUntilUtc,
                    x.SourceSelectorDefinitionId,
                    x.Explanation,
                    x.ProvenanceJson))
                .ToList());
    }

    public async Task<SourceSystemEventAcceptedResult> IngestSourceSystemEventAsync(
        SourceSystemEventInput input,
        CancellationToken cancellationToken)
    {
        await sourceSystemEventValidator.ValidateAndThrowAsync(input, cancellationToken);
        var tenant = await GetTenantAsync(input.TenantSlug, cancellationToken);
        var actor = currentActorService.GetCurrentActor();
        var utcNow = clock.UtcNow;
        var observedAtUtc = input.ObservedAtUtc ?? utcNow;
        var normalizedSourceSystem = input.SourceSystem.Trim();
        var normalizedEventType = input.EventType.Trim();
        var normalizedEventId = input.EventId.Trim();
        var workspace = await ResolveWorkspaceForEventAsync(tenant.Id, input.WorkspaceSlug, cancellationToken);

        // Cross-instance idempotency is enforced at the PostgreSQL boundary.
        // A transaction-scoped advisory lock serialises only requests for the
        // same logical event across every Scout instance. Once the lock is
        // held the duplicate check is authoritative for this transaction.
        var usesPostgres = string.Equals(
            dbContext.Database.ProviderName,
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            StringComparison.Ordinal);
        await using var eventTransaction = usesPostgres
            ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        if (eventTransaction is not null)
        {
            var eventLockKey = $"{tenant.Id:D}|{normalizedSourceSystem}|{normalizedEventId}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({eventLockKey}, 0));",
                cancellationToken);
        }

        async Task PersistEventAsync()
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (eventTransaction is not null)
            {
                await eventTransaction.CommitAsync(cancellationToken);
            }
        }

        var duplicate = await dbContext.SourceSystemEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TenantId == tenant.Id
                    && x.SourceSystem == normalizedSourceSystem
                    && x.EventId == normalizedEventId,
                cancellationToken);
        if (duplicate is not null)
        {
            var sameLogicalEvent =
                string.Equals(duplicate.EventType, normalizedEventType, StringComparison.Ordinal)
                && string.Equals(duplicate.ExternalUserId?.Trim(), input.ExternalUserId?.Trim(), StringComparison.Ordinal)
                && string.Equals(duplicate.ExternalAccountId?.Trim(), input.ExternalAccountId?.Trim(), StringComparison.Ordinal)
                && duplicate.WorkspaceId == workspace?.Id
                // An explicitly supplied data source is part of immutable event identity.
                // Older/retry callers may omit it, in which case the source-system/event-id
                // identity remains authoritative and the stored binding is retained.
                && (!input.DataSourceId.HasValue || duplicate.DataSourceId == input.DataSourceId)
                // ObservedAtUtc describes when the source observed the event, not delivery
                // identity. Retries may legitimately carry a different observation timestamp;
                // the first retained event remains the authoritative evidence record.
                && JsonPayloadsEquivalent(duplicate.PayloadJson, input.PayloadJson);
            if (!sameLogicalEvent)
            {
                throw new SourceSystemEventConflictException(normalizedSourceSystem, normalizedEventId);
            }

            dbContext.AuditEvents.Add(AuditEvent.Create(
                tenant.Id,
                actor.Email,
                "source-system.event.ignored",
                nameof(SourceSystemEvent),
                duplicate.Id.ToString("D"),
                duplicate.CorrelationId,
                JsonSerializer.Serialize(new { reason = "duplicate", duplicate.EventId, duplicate.SourceSystem }),
                null,
                null,
                utcNow));
            await PersistEventAsync();

            return new SourceSystemEventAcceptedResult(
                duplicate.EventId,
                tenant.Id,
                tenant.Slug,
                duplicate.WorkspaceId,
                duplicate.UserProfileId,
                duplicate.UserProfileId is null ? 0 : 1,
                duplicate.MatchedSelectorCount,
                duplicate.Status.ToString(),
                IsDuplicate: true,
                utcNow);
        }

        await billingEnforcementService.EnsureWithinLimitAsync(tenant.Id, BillingLimitMetric.SourceEvents, 1, cancellationToken, workspace?.Id);
        var user = await ResolveUserForEventAsync(tenant, input.ExternalUserId, input.ExternalAccountId, cancellationToken);
        var dataSource = input.DataSourceId.HasValue
            ? await ResolveDataSourceForEventAsync(tenant.Id, input.DataSourceId.Value, cancellationToken)
                ?? throw new InvalidOperationException($"Data source '{input.DataSourceId.Value}' was not found.")
            : await ResolveDataSourceForEventAsync(tenant.Id, normalizedSourceSystem, cancellationToken);
        var correlationId = Guid.NewGuid().ToString("N");
        var sourceEvent = SourceSystemEvent.Create(
            tenant.Id,
            workspace?.Id,
            normalizedEventId,
            normalizedSourceSystem,
            normalizedEventType,
            input.ExternalUserId,
            input.ExternalAccountId,
            user?.Id,
            dataSource?.Id,
            input.PayloadJson,
            JsonSerializer.Serialize(new
            {
                actor.SubjectId,
                actor.Email,
                workspaceSlug = workspace?.Slug
            }),
            correlationId,
            observedAtUtc,
            utcNow);

        dbContext.SourceSystemEvents.Add(sourceEvent);
        dbContext.AuditEvents.Add(AuditEvent.Create(
            tenant.Id,
            actor.Email,
            "source-system.event.received",
            nameof(SourceSystemEvent),
            sourceEvent.Id.ToString("D"),
            correlationId,
            JsonSerializer.Serialize(new
            {
                sourceEvent.EventId,
                sourceEvent.SourceSystem,
                sourceEvent.EventType,
                sourceEvent.ExternalUserId,
                sourceEvent.ExternalAccountId,
                workspaceSlug = workspace?.Slug
            }),
            null,
            input.PayloadJson,
            utcNow));
        await usageMeteringService.RecordAsync(
            new UsageRecordInput(
                tenant.Id,
                workspace?.Id,
                BillingUsageMetric.SourceEventIngested,
                1,
                "source-system-event",
                new { sourceEvent.EventId, sourceEvent.SourceSystem, sourceEvent.EventType, correlationId }),
            cancellationToken,
            saveImmediately: false);

        if (string.Equals(normalizedEventType, "source_record.deleted", StringComparison.OrdinalIgnoreCase))
        {
            sourceEvent.MarkIgnored("Delete events are recorded for audit, but selectors are not recomputed automatically.", utcNow);
            dbContext.AuditEvents.Add(AuditEvent.Create(
                tenant.Id,
                actor.Email,
                "source-system.event.ignored",
                nameof(SourceSystemEvent),
                sourceEvent.Id.ToString("D"),
                correlationId,
                JsonSerializer.Serialize(new { reason = "source-record-deleted", sourceEvent.EventId }),
                null,
                null,
                utcNow));
            await PersistEventAsync();
            return MapEventAccepted(sourceEvent, tenant, storedSignalCount: 0, isDuplicate: false, utcNow);
        }

        if (user is null)
        {
            var reason = "No matching user profile was found for the event routing keys.";
            sourceEvent.MarkFailed(reason, utcNow);
            sourceEvent.MarkDeadLettered(reason, utcNow);
            dbContext.AuditEvents.Add(AuditEvent.Create(
                tenant.Id,
                actor.Email,
                "source-system.event.failed",
                nameof(SourceSystemEvent),
                sourceEvent.Id.ToString("D"),
                correlationId,
                JsonSerializer.Serialize(new { reason, sourceEvent.EventId, sourceEvent.ExternalUserId, sourceEvent.ExternalAccountId }),
                null,
                null,
                utcNow));
            await PersistEventAsync();
            return MapEventAccepted(sourceEvent, tenant, storedSignalCount: 0, isDuplicate: false, utcNow);
        }

        dbContext.UserSignals.Add(UserSignal.Create(
            tenant.Id,
            user.Id,
            dataSource?.Id,
            $"{normalizedSourceSystem}.{normalizedEventType}",
            input.PayloadJson,
            FactValueType.Json,
            observedAtUtc,
            JsonSerializer.Serialize(new
            {
                eventId = sourceEvent.EventId,
                sourceSystem = normalizedSourceSystem,
                eventType = normalizedEventType,
                externalAccountId = input.ExternalAccountId?.Trim(),
                sourceSystemEventId = sourceEvent.Id
            }),
            utcNow));

        var matchedSelectors = await MatchSelectorsForEventAsync(tenant.Id, dataSource?.Id, normalizedSourceSystem, normalizedEventType, cancellationToken);
        if (matchedSelectors.Count == 0)
        {
            sourceEvent.MarkProcessed(0, "Event stored as a user signal; no published selectors matched the source trigger.", utcNow);
            dbContext.AuditEvents.Add(AuditEvent.Create(
                tenant.Id,
                actor.Email,
                "source-system.event.processed",
                nameof(SourceSystemEvent),
                sourceEvent.Id.ToString("D"),
                correlationId,
                JsonSerializer.Serialize(new { sourceEvent.EventId, matchedSelectors = 0, queuedRecompute = false }),
                null,
                null,
                utcNow));
            await PersistEventAsync();
            return MapEventAccepted(sourceEvent, tenant, storedSignalCount: 1, isDuplicate: false, utcNow);
        }

        var executions = matchedSelectors
            .Select(selector => SelectorExecution.Create(tenant.Id, selector.Id, user.Id, correlationId, $"webhook:{normalizedSourceSystem}", SelectorExecutionMode.Live, utcNow))
            .ToList();
        var recomputeJob = RecomputeJob.Create(
            tenant.Id,
            user.Id,
            correlationId,
            $"webhook:{normalizedSourceSystem}",
            executions.Count,
            $"Webhook event '{normalizedEventType}' queued {executions.Count} selector executions for {user.ExternalUserId}.",
            JsonSerializer.Serialize(new
            {
                sourceSystemEventId = sourceEvent.Id,
                sourceEvent.EventId,
                sourceEvent.SourceSystem,
                sourceEvent.EventType,
                selectorIds = executions.Select(x => x.SelectorDefinitionId)
            }),
            utcNow);
        dbContext.SelectorExecutions.AddRange(executions);
        dbContext.RecomputeJobs.Add(recomputeJob);
        sourceEvent.MarkProcessed(executions.Count, $"Queued {executions.Count} selector executions.", utcNow);
        dbContext.AuditEvents.Add(AuditEvent.Create(
            tenant.Id,
            actor.Email,
            "source-system.event.processed",
            nameof(SourceSystemEvent),
            sourceEvent.Id.ToString("D"),
            correlationId,
            JsonSerializer.Serialize(new { sourceEvent.EventId, matchedSelectors = executions.Count, queuedRecompute = true }),
            null,
            null,
            utcNow));
        await PersistEventAsync();
        await recomputeQueue.EnqueueAsync(new ContextRecomputeRequest(tenant.Id, user.Id, correlationId, executions.Select(x => x.Id).ToList()), cancellationToken);

        return MapEventAccepted(sourceEvent, tenant, storedSignalCount: 1, isDuplicate: false, utcNow);
    }

    public async Task<IReadOnlyList<SourceSystemEventHistoryResult>> GetSourceSystemEventsAsync(
        string tenantSlug,
        string? workspaceSlug,
        string? sourceSystem,
        string? eventType,
        string? status,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        var tenant = await GetTenantAsync(tenantSlug, cancellationToken);
        var query = dbContext.SourceSystemEvents
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id);

        if (!string.IsNullOrWhiteSpace(workspaceSlug))
        {
            var workspace = await ResolveWorkspaceForEventAsync(tenant.Id, workspaceSlug, cancellationToken)
                ?? throw new InvalidOperationException($"Workspace '{workspaceSlug}' was not found.");
            query = query.Where(x => x.WorkspaceId == workspace.Id);
        }
        if (!string.IsNullOrWhiteSpace(sourceSystem))
        {
            query = query.Where(x => x.SourceSystem == sourceSystem.Trim());
        }
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(x => x.EventType == eventType.Trim());
        }
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<SourceSystemEventStatus>(status.Trim(), true, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }
        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.ReceivedAtUtc >= fromUtc.Value);
        }
        if (toUtc.HasValue)
        {
            query = query.Where(x => x.ReceivedAtUtc <= toUtc.Value);
        }

        return await query
            .OrderByDescending(x => x.ReceivedAtUtc)
            .Take(500)
            .Select(x => new SourceSystemEventHistoryResult(
                x.Id,
                x.TenantId,
                x.WorkspaceId,
                x.EventId,
                x.SourceSystem,
                x.EventType,
                x.Status.ToString(),
                x.ExternalUserId,
                x.ExternalAccountId,
                x.UserProfileId,
                x.DataSourceId,
                x.MatchedSelectorCount,
                x.ProcessingSummary,
                x.ErrorMessage,
                x.DeadLetterReason,
                x.CorrelationId,
                x.ReceivedAtUtc,
                x.ObservedAtUtc,
                x.ProcessedAtUtc,
                x.DeadLetteredAtUtc,
                x.PayloadJson))
            .ToListAsync(cancellationToken);
    }

    public async Task<SalesContextPackageResult?> GetSalesContextPackageAsync(SalesContextPackageInput input, CancellationToken cancellationToken)
    {
        await salesContextPackageValidator.ValidateAndThrowAsync(input, cancellationToken);
        var tenant = await GetTenantAsync(input.TenantSlug, cancellationToken);
        var user = await dbContext.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.ExternalUserId == input.ExternalUserId.Trim(), cancellationToken);

        if (user is null)
        {
            return null;
        }

        var snapshot = await GetLatestContextSnapshotAsync(tenant.Id, user.Id, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        await WriteReadAuditAsync(
            tenant.Id,
            actor: currentActorService.GetCurrentActor(),
            action: "sales-context.read",
            entityType: nameof(UserProfile),
            entityId: user.Id,
            metadata: new
            {
                user.ExternalUserId,
                input.SalesObjective,
                snapshotId = snapshot.Id
            },
            cancellationToken);

        return salesSupportAgentService.BuildContextPackage(
            tenant,
            user,
            snapshot,
            input.SalesObjective,
            clock.UtcNow);
    }

    public Task<AgentRunResult> CreateAgentRunAsync(
        CreateAgentRunInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException(
            "Scout core does not execute AI models. Use GetSalesContextPackageAsync or the versioned context APIs " +
            "to obtain governed context, then invoke the model from a customer-owned/reference consumer.");
    }

    public async Task<SelectorExecutionPreviewResult> PreviewSelectorAsync(PreviewSelectorInput input, CancellationToken cancellationToken)
    {
        await previewSelectorValidator.ValidateAndThrowAsync(input, cancellationToken);
        var tenant = await GetTenantAsync(input.TenantSlug, cancellationToken);
        var user = await GetUserProfileAsync(tenant.Id, input.ExternalUserId, cancellationToken);
        var runtimeContext = input.SelectorDefinitionId.HasValue
            ? await BuildRuntimeContextAsync(tenant, input.SelectorDefinitionId.Value, cancellationToken)
            : await BuildRuntimeContextFromDraftAsync(tenant, input.DraftSelector!, cancellationToken);

        var outcome = await selectorExecutionEngine.ExecuteAsync(runtimeContext, user, SelectorExecutionMode.Preview, cancellationToken);
        return MapPreviewResult(outcome);
    }

    public async Task<SelectorValidationResult> ValidateSelectorAsync(ValidateSelectorInput input, CancellationToken cancellationToken)
    {
        await validateSelectorValidator.ValidateAndThrowAsync(input, cancellationToken);
        var tenant = await GetTenantAsync(input.TenantSlug, cancellationToken);
        var runtimeContext = await BuildRuntimeContextFromDraftAsync(tenant, input.DraftSelector, cancellationToken);
        UserProfile? user = null;
        if (!string.IsNullOrWhiteSpace(input.ExternalUserId))
        {
            user = await GetUserProfileAsync(tenant.Id, input.ExternalUserId, cancellationToken);
        }

        var outcome = await selectorExecutionEngine.ValidateAsync(runtimeContext, user, cancellationToken);
        return new SelectorValidationResult(
            outcome.IsSuccess,
            outcome.ValidationErrors,
            outcome.RawSourceDataJson,
            outcome.NormalizedSourceDataJson,
            outcome.PipelineTraceJson);
    }

    public async Task<ScheduledRecomputeDispatchResult> RunScheduledRecomputeAsync(RunScheduledRecomputeInput input, CancellationToken cancellationToken)
    {
        await runScheduledRecomputeValidator.ValidateAndThrowAsync(input, cancellationToken);
        return await scheduledRecomputeDispatcher.DispatchDueUsersAsync(input.TenantSlug, cancellationToken);
    }

    private async Task<KynticAI.Scout.Domain.Saas.Workspace?> ResolveWorkspaceForEventAsync(
        Guid tenantId,
        string? workspaceSlug,
        CancellationToken cancellationToken)
    {
        KynticAI.Scout.Domain.Saas.Workspace? workspace;
        if (string.IsNullOrWhiteSpace(workspaceSlug))
        {
            workspace = await dbContext.Workspaces
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Status == WorkspaceStatus.Active)
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            var normalizedWorkspaceSlug = workspaceSlug.Trim().ToLowerInvariant();
            workspace = await dbContext.Workspaces
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.TenantId == tenantId && x.Slug == normalizedWorkspaceSlug && x.Status == WorkspaceStatus.Active,
                    cancellationToken);
        }

        var actor = currentActorService.GetCurrentActor();
        if (workspace is not null
            && !actor.IsSystem
            && !actor.IsPlatformOwner
            && actor.WorkspaceId is not null
            && actor.WorkspaceId != workspace.Id)
        {
            throw new UnauthorizedAccessException("Workspace access is not permitted for this event.");
        }

        return workspace;
    }

    private async Task<UserProfile?> ResolveUserForEventAsync(
        Tenant tenant,
        string? externalUserId,
        string? externalAccountId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(externalUserId))
        {
            return await dbContext.UserProfiles
                .FirstOrDefaultAsync(
                    x => x.TenantId == tenant.Id && x.ExternalUserId == externalUserId.Trim(),
                    cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(externalAccountId))
        {
            return null;
        }

        var resolvedExternalUserId = await operationalReferenceDataProvider.ResolveExternalUserIdByAccountAsync(
            tenant.Slug,
            externalAccountId,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(resolvedExternalUserId))
        {
            return null;
        }

        return await dbContext.UserProfiles
            .FirstOrDefaultAsync(
                x => x.TenantId == tenant.Id && x.ExternalUserId == resolvedExternalUserId,
                cancellationToken);
    }

    private async Task<DataSource?> ResolveDataSourceForEventAsync(
        Guid tenantId,
        string sourceSystem,
        CancellationToken cancellationToken)
    {
        var normalizedSource = sourceSystem.Trim().ToLowerInvariant();
        var dataSources = await dbContext.DataSources
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        return dataSources.FirstOrDefault(x =>
            x.Name.Contains(normalizedSource, StringComparison.OrdinalIgnoreCase)
            || x.ConnectionConfigJson.Contains(normalizedSource, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<DataSource?> ResolveDataSourceForEventAsync(
        Guid tenantId,
        Guid dataSourceId,
        CancellationToken cancellationToken)
        => await dbContext.DataSources
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.Id == dataSourceId,
                cancellationToken);

    private async Task<IReadOnlyList<SelectorDefinition>> MatchSelectorsForEventAsync(
        Guid tenantId,
        Guid? dataSourceId,
        string sourceSystem,
        string eventType,
        CancellationToken cancellationToken)
    {
        var normalizedSource = sourceSystem.Trim();
        var normalizedEventType = eventType.Trim();
        var selectors = await dbContext.SelectorDefinitions
            .Include(x => x.DataSource)
            .Where(x => x.TenantId == tenantId && x.Status == SelectorStatus.Published)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return selectors
            .Where(selector =>
                (dataSourceId.HasValue && selector.DataSourceId == dataSourceId)
                || selector.DataSource?.Name.Contains(normalizedSource, StringComparison.OrdinalIgnoreCase) == true
                || selector.DataSource?.ConnectionConfigJson.Contains(normalizedSource, StringComparison.OrdinalIgnoreCase) == true
                || selector.ExpressionJson.Contains(normalizedEventType, StringComparison.OrdinalIgnoreCase)
                || selector.ExpressionJson.Contains(normalizedSource, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static SourceSystemEventAcceptedResult MapEventAccepted(
        SourceSystemEvent sourceEvent,
        Tenant tenant,
        int storedSignalCount,
        bool isDuplicate,
        DateTime acceptedAtUtc)
        => new(
            sourceEvent.EventId,
            tenant.Id,
            tenant.Slug,
            sourceEvent.WorkspaceId,
            sourceEvent.UserProfileId,
            storedSignalCount,
            sourceEvent.MatchedSelectorCount,
            sourceEvent.Status.ToString(),
            isDuplicate,
            acceptedAtUtc);

    private async Task<Tenant> GetTenantAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        var normalizedSlug = NormalizeSlug(tenantSlug);
        var actor = currentActorService.GetCurrentActor();
        if (!actor.IsSystem
            && !actor.IsPlatformOwner
            && !string.Equals(actor.TenantSlug, normalizedSlug, StringComparison.Ordinal))
        {
            dbContext.AuditEvents.Add(AuditEvent.Create(
                actor.TenantId,
                actor.Email,
                "auth.permission.denied",
                nameof(Tenant),
                normalizedSlug,
                Guid.NewGuid().ToString("N"),
                JsonSerializer.Serialize(new { reason = "cross-tenant-access", requestedTenantSlug = normalizedSlug, actor.TenantSlug }),
                null,
                null,
                clock.UtcNow));
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("Cross-tenant access is not permitted.");
        }

        return await dbContext.Tenants.FirstOrDefaultAsync(x => x.Slug == normalizedSlug, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{tenantSlug}' was not found.");
    }

    private async Task WriteAdminPermissionDeniedAsync(
        Guid tenantId,
        string actor,
        string action,
        Guid entityId,
        string reason,
        CancellationToken cancellationToken)
    {
        dbContext.AuditEvents.Add(AuditEvent.Create(
            tenantId,
            actor,
            "auth.permission.denied",
            nameof(OperatorAccount),
            entityId.ToString("D"),
            Guid.NewGuid().ToString("N"),
            JsonSerializer.Serialize(new { action, reason }),
            null,
            null,
            clock.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<UserProfile> GetUserProfileAsync(Guid tenantId, string externalUserId, CancellationToken cancellationToken)
    {
        return await dbContext.UserProfiles
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ExternalUserId == externalUserId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"User '{externalUserId}' was not found.");
    }

    private async Task<ContextSnapshot?> GetLatestContextSnapshotAsync(Guid tenantId, Guid userProfileId, CancellationToken cancellationToken)
    {
        return await dbContext.ContextSnapshots
            .Include(x => x.Facts)
                .ThenInclude(x => x.SemanticAttributeDefinition)
            .Include(x => x.Facts)
                .ThenInclude(x => x.SourceSelectorDefinition)
            .OrderByDescending(x => x.GeneratedAtUtc)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserProfileId == userProfileId, cancellationToken);
    }

    private Task<OperationalSourceSummaryResult?> BuildOperationalSourceSummaryAsync(
        string tenantSlug,
        string externalUserId,
        CancellationToken cancellationToken)
        => operationalReferenceDataProvider.GetSourceSummaryAsync(
            tenantSlug,
            externalUserId,
            currentActorService.GetCurrentActor().CanViewSensitivePii,
            cancellationToken);

    private async Task<SelectorRuntimeContext> BuildRuntimeContextAsync(Tenant tenant, Guid selectorDefinitionId, CancellationToken cancellationToken)
    {
        var selector = await dbContext.SelectorDefinitions
            .Include(x => x.DataSource)
            .Include(x => x.TargetAttributeDefinition)
            .FirstOrDefaultAsync(x => x.Id == selectorDefinitionId && x.TenantId == tenant.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Selector '{selectorDefinitionId}' was not found.");
        var dataSource = selector.DataSource ?? throw new InvalidOperationException($"Selector '{selector.Name}' does not reference a data source.");
        return new SelectorRuntimeContext(selector, dataSource, selector.TargetAttributeDefinition);
    }

    private async Task<SelectorRuntimeContext> BuildRuntimeContextFromDraftAsync(
        Tenant tenant,
        UpsertSelectorDefinitionInput draftSelector,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(tenant.Slug, NormalizeSlug(draftSelector.TenantSlug), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Draft selector tenant slug does not match the target tenant.");
        }

        var targetAttribute = await dbContext.SemanticAttributeDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == draftSelector.TargetAttributeDefinitionId && x.TenantId == tenant.Id, cancellationToken)
            ?? throw new InvalidOperationException("Draft selector target attribute does not exist in the tenant.");

        var dataSource = draftSelector.DataSourceId.HasValue
            ? await dbContext.DataSources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == draftSelector.DataSourceId && x.TenantId == tenant.Id, cancellationToken)
            : null;

        if (dataSource is null)
        {
            throw new InvalidOperationException("Draft selectors require a data source.");
        }

        var selector = SelectorDefinition.Create(
            tenant.Id,
            draftSelector.DataSourceId,
            draftSelector.TargetAttributeDefinitionId,
            draftSelector.Name,
            draftSelector.Description,
            draftSelector.MappingKind,
            draftSelector.ExpressionJson,
            draftSelector.ExplanationTemplate,
            draftSelector.ValidationSchemaJson,
            draftSelector.DefaultConfidence,
            draftSelector.FreshnessWindowMinutes,
            draftSelector.Priority,
            draftSelector.ScheduleIntervalMinutes,
            clock.UtcNow);

        return new SelectorRuntimeContext(selector, dataSource, targetAttribute);
    }

    private async Task EnsureValidConnectorConfigurationAsync(
        IConnectorPlugin plugin,
        DataSourceKind kind,
        JsonObject configuration,
        JsonObject credentials,
        CancellationToken cancellationToken)
    {
        var validation = await plugin.ValidateConfigurationAsync(
            new ConnectorConfigurationValidationRequest(plugin.ConnectorType, kind, configuration, credentials),
            cancellationToken);
        if (validation.IsValid)
        {
            return;
        }

        throw new ValidationException(validation.Errors.Select(error => new ValidationFailure("ConfigurationJson", error)));
    }

    private static JsonObject ParseJsonObject(string? json, string fieldName, bool allowEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return allowEmpty ? new JsonObject() : throw new InvalidOperationException($"{fieldName} is required.");
        }

        var node = JsonNode.Parse(json);
        return node as JsonObject ?? throw new InvalidOperationException($"{fieldName} must contain a JSON object.");
    }

    private static JsonObject SanitizeConfiguration(JsonObject configuration)
    {
        var clone = configuration.DeepClone() as JsonObject ?? new JsonObject();
        if (clone["credentials"] is JsonObject credentials)
        {
            foreach (var key in credentials.Select(static item => item.Key).ToList())
            {
                credentials[key] = "***";
            }
        }

        return clone;
    }

    private static ConnectorRunMode ParseConnectorRunMode(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "preview" => ConnectorRunMode.Preview,
            "dryrun" or "dry_run" => ConnectorRunMode.DryRun,
            "scheduled" or "scheduledsync" or "scheduled_sync" => ConnectorRunMode.ScheduledSync,
            "eventtriggered" or "event_triggered" => ConnectorRunMode.EventTriggeredRecompute,
            _ => ConnectorRunMode.Live
        };

    private static string NormalizeSlug(string tenantSlug) => tenantSlug.Trim().ToLowerInvariant();

    private static string NormalizeRoleName(string role)
        => role.Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

    private static OperatorAccountSummaryResult MapOperatorAccount(OperatorAccount account)
        => new(
            account.Id,
            account.TenantId,
            account.Email,
            account.DisplayName,
            account.Role.ToString(),
            account.IsActive,
            account.LastLoginAtUtc,
            account.CreatedAtUtc,
            account.UpdatedAtUtc,
            account.WorkspaceMemberships
                .OrderBy(membership => membership.Workspace.Name)
                .Select(membership => new OperatorWorkspaceMembershipResult(
                    membership.WorkspaceId,
                    membership.Workspace.Slug,
                    membership.Workspace.Name,
                    membership.Role.ToString(),
                    membership.AcceptedAtUtc))
                .ToList());

    private static int CountJsonArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return document.RootElement.GetArrayLength();
            }

            if (document.RootElement.TryGetProperty("changes", out var changes)
                && changes.ValueKind == JsonValueKind.Array)
            {
                return changes.GetArrayLength();
            }

            return 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static string BuildAuditCsv(IReadOnlyList<AuditEvent> events)
    {
        var builder = new StringBuilder();
        builder.AppendLine("createdAtUtc,actor,action,entityType,entityId,correlationId,metadataJson");
        foreach (var auditEvent in events)
        {
            builder
                .Append(Csv(auditEvent.CreatedAtUtc.ToString("O"))).Append(',')
                .Append(Csv(auditEvent.Actor)).Append(',')
                .Append(Csv(auditEvent.Action)).Append(',')
                .Append(Csv(auditEvent.EntityType)).Append(',')
                .Append(Csv(auditEvent.EntityId)).Append(',')
                .Append(Csv(auditEvent.CorrelationId)).Append(',')
                .Append(Csv(auditEvent.MetadataJson))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string Csv(string? value)
    {
        // CSV formula-injection guard: cells that begin with =, +, -, @, tab or carriage
        // return are prefixed with a single quote so spreadsheet applications treat them
        // as text rather than evaluating a formula or external link.
        var raw = value ?? string.Empty;
        if (raw.Length > 0 && (raw[0] is '=' or '+' or '-' or '@' or '\t' or '\r'))
        {
            raw = "'" + raw;
        }

        var escaped = raw.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static SelectorExecutionPreviewResult MapPreviewResult(SelectorPipelineOutcome outcome)
    {
        return new SelectorExecutionPreviewResult(
            outcome.Mode,
            outcome.IsSuccess,
            outcome.SelectorName,
            outcome.RawSourceDataJson,
            outcome.NormalizedSourceDataJson,
            outcome.ValidationErrors,
            outcome.CandidateFact?.ValueJson,
            outcome.CandidateFact?.ValueType,
            outcome.CandidateFact?.Confidence,
            outcome.CandidateFact?.ObservedAtUtc,
            outcome.CandidateFact?.FreshUntilUtc,
            outcome.CandidateFact?.Explanation,
            outcome.CandidateFact?.ProvenanceJson,
            outcome.PipelineTraceJson);
    }

    private static AuditEvent CreateAuditEvent(
        Guid? tenantId,
        string actor,
        string action,
        string entityType,
        Guid entityId,
        string? beforeJson,
        string? afterJson,
        DateTime utcNow)
    {
        return AuditEvent.Create(tenantId, actor, action, entityType, entityId.ToString("D"), Guid.NewGuid().ToString("N"), "{}", beforeJson, afterJson, utcNow);
    }

    private async Task WriteReadAuditAsync(
        Guid tenantId,
        ActorContext actor,
        string action,
        string entityType,
        Guid entityId,
        object metadata,
        CancellationToken cancellationToken)
    {
        dbContext.AuditEvents.Add(CreateAuditEvent(
            tenantId,
            actor.Email,
            action,
            entityType,
            entityId,
            null,
            JsonSerializer.Serialize(metadata),
            clock.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool JsonPayloadsEquivalent(string left, string right)
    {
        try
        {
            var leftNode = JsonNode.Parse(left);
            var rightNode = JsonNode.Parse(right);
            return JsonNode.DeepEquals(leftNode, rightNode);
        }
        catch (JsonException)
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
        }
    }

    private static string MaskEmail(string email)
    {
        var separatorIndex = email.IndexOf('@');
        if (separatorIndex <= 1)
        {
            return "***";
        }

        var local = email[..separatorIndex];
        var domain = email[separatorIndex..];
        return $"{local[0]}***{domain}";
    }

    private static IReadOnlyList<string> DeserializeStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonSerializerOptions.Web) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static readonly JsonSerializerOptions AuditSerializerOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static string Serialize<T>(T instance) => JsonSerializer.Serialize(instance, AuditSerializerOptions);
}
