using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Domain.Saas;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.UnitTests;

public sealed class ScoutServiceSaasArchitectureOverviewTests
{
    [Fact]
    public async Task SaasArchitectureOverview_ReturnsExpectedShape_FromPersistenceState()
    {
        await using var harness = await ScoutServiceTestHarness.CreateAsync("BackendOnly", "open-core-apis");
        var tenant = await harness.DbContext.Tenants.SingleAsync();
        var utcNow = harness.Clock.UtcNow;

        harness.DbContext.TenantSubscriptions.Add(TenantSubscription.Create(
            tenant.Id,
            SubscriptionPlan.Pro,
            SubscriptionStatus.Active,
            "cust-123",
            """{"seats":10}""",
            utcNow.AddDays(-30),
            null,
            utcNow.AddDays(30),
            utcNow));

        var workspace = Workspace.Create(tenant.Id, "primary", "Primary Workspace", "description", isDefault: true, utcNow);
        harness.DbContext.Workspaces.Add(workspace);
        var operatorAccount = OperatorAccount.Create(tenant.Id, "owner@scout.local", "Owner", "hash", OperatorRole.TenantAdmin, utcNow);
        harness.DbContext.OperatorAccounts.Add(operatorAccount);
        harness.DbContext.WorkspaceMembers.Add(WorkspaceMember.Create(tenant.Id, workspace.Id, operatorAccount.Id, WorkspaceMemberRole.Owner, utcNow));
        harness.DbContext.WorkspaceMembers.Add(WorkspaceMember.Create(tenant.Id, workspace.Id, Guid.NewGuid(), WorkspaceMemberRole.Member, utcNow));

        var dataSource = DataSource.Create(tenant.Id, "CRM", "crm", DataSourceKind.Crm, """{"connectorType":"mockCrm"}""", utcNow);
        harness.DbContext.DataSources.Add(dataSource);
        harness.DbContext.ConnectorInstallations.Add(ConnectorInstallation.Create(
            tenant.Id,
            workspace.Id,
            dataSource.Id,
            "mockCrm",
            "[]",
            utcNow));

        harness.DbContext.OnboardingStates.Add(OnboardingState.Create(tenant.Id, workspace.Id, "connect-source", OnboardingStepStatus.Completed, "{}", utcNow));
        harness.DbContext.OnboardingStates.Add(OnboardingState.Create(tenant.Id, workspace.Id, "map-attributes", OnboardingStepStatus.InProgress, "{}", utcNow));

        harness.DbContext.ApiClients.Add(ApiClient.Create(
            tenant.Id,
            workspace.Id,
            "svc-1",
            "Service One",
            "hash",
            """["context:read"]""",
            utcNow));

        harness.DbContext.BillingUsageRecords.Add(BillingUsageRecord.Create(
            tenant.Id,
            workspace.Id,
            BillingUsageMetric.SelectorExecution,
            3,
            utcNow.AddDays(-1),
            utcNow,
            "selector",
            "{}",
            utcNow));
        harness.DbContext.BillingUsageRecords.Add(BillingUsageRecord.Create(
            tenant.Id,
            workspace.Id,
            BillingUsageMetric.SelectorExecution,
            4,
            utcNow.AddDays(-1),
            utcNow,
            "selector",
            "{}",
            utcNow));
        await harness.DbContext.SaveChangesAsync();

        var overview = await harness.Service.GetSaasArchitectureOverviewAsync(tenant.Slug, CancellationToken.None);

        Assert.Equal(tenant.Id, overview.TenantId);
        Assert.Equal("demo", overview.TenantSlug);
        Assert.Equal("Demo Tenant", overview.TenantName);
        Assert.Equal("BackendOnly", overview.Mode);
        Assert.Equal(new[] { "open-core-apis" }, overview.EnabledFeatureFlags);

        var subscription = Assert.IsType<SaasSubscriptionSummaryResult>(overview.Subscription);
        Assert.Equal("Pro", subscription.Plan);
        Assert.Equal("Active", subscription.Status);
        Assert.Equal("cust-123", subscription.BillingCustomerReference);

        var workspaceSummary = Assert.Single(overview.Workspaces);
        Assert.Equal("primary", workspaceSummary.Slug);
        Assert.Equal("Primary Workspace", workspaceSummary.Name);
        Assert.Equal("Active", workspaceSummary.Status);
        Assert.True(workspaceSummary.IsDefault);
        Assert.Equal(2, workspaceSummary.MemberCount);
        Assert.Equal(1, workspaceSummary.ConnectorCount);
        Assert.Equal(1, workspaceSummary.OnboardingCompletedSteps);
        Assert.Equal(2, workspaceSummary.OnboardingTotalSteps);

        var apiClient = Assert.Single(overview.ApiClients);
        Assert.Equal("svc-1", apiClient.ClientId);
        Assert.Equal("Service One", apiClient.DisplayName);
        Assert.Equal(new[] { "context:read" }, apiClient.Scopes);

        var usage = Assert.Single(overview.Usage);
        Assert.Equal("SelectorExecution", usage.Metric);
        Assert.Equal(7, usage.Quantity);
    }

    [Fact]
    public async Task SaasArchitectureOverview_EmptyTenant_YieldsNonMisleadingSummary()
    {
        await using var harness = await ScoutServiceTestHarness.CreateAsync("SaaS", "open-core-apis", "hosted-billing-usage");
        var tenant = await harness.DbContext.Tenants.SingleAsync();

        var overview = await harness.Service.GetSaasArchitectureOverviewAsync(tenant.Slug, CancellationToken.None);

        Assert.Equal(tenant.Id, overview.TenantId);
        Assert.Equal("demo", overview.TenantSlug);
        Assert.Equal("SaaS", overview.Mode);
        Assert.Equal(new[] { "open-core-apis", "hosted-billing-usage" }, overview.EnabledFeatureFlags);
        Assert.Null(overview.Subscription);
        Assert.Empty(overview.Workspaces);
        Assert.Empty(overview.ApiClients);
        Assert.Empty(overview.Usage);
    }
}
