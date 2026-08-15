using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Saas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace KynticAI.Scout.Application.Abstractions;

public interface IScoutDbContext
{
    DatabaseFacade Database { get; }

    DbSet<Tenant> Tenants { get; }

    DbSet<UserProfile> UserProfiles { get; }

    DbSet<OperatorAccount> OperatorAccounts { get; }

    DbSet<DataSource> DataSources { get; }

    DbSet<SemanticAttributeDefinition> SemanticAttributeDefinitions { get; }

    DbSet<SelectorDefinition> SelectorDefinitions { get; }

    DbSet<SelectorExecution> SelectorExecutions { get; }

    DbSet<ContextSnapshot> ContextSnapshots { get; }

    DbSet<ContextFact> ContextFacts { get; }

    DbSet<PromptTemplate> PromptTemplates { get; }

    DbSet<AgentRun> AgentRuns { get; }

    DbSet<AuditEvent> AuditEvents { get; }

    DbSet<RecomputeJob> RecomputeJobs { get; }

    DbSet<ProvenanceMetadata> ProvenanceMetadata { get; }

    DbSet<ConnectorCredential> ConnectorCredentials { get; }

    DbSet<SourceSystemEvent> SourceSystemEvents { get; }

    DbSet<UserSignal> UserSignals { get; }

    DbSet<Workspace> Workspaces { get; }

    DbSet<WorkspaceMember> WorkspaceMembers { get; }

    DbSet<TenantSubscription> TenantSubscriptions { get; }

    DbSet<BillingPlan> BillingPlans { get; }

    DbSet<BillingPlanLimit> BillingPlanLimits { get; }

    DbSet<ApiClient> ApiClients { get; }

    DbSet<WebhookSigningSecret> WebhookSigningSecrets { get; }

    DbSet<ConnectorInstallation> ConnectorInstallations { get; }

    DbSet<ConnectorCatalogueEntry> ConnectorCatalogueEntries { get; }

    DbSet<ContextPackage> ContextPackages { get; }

    DbSet<BillingUsageRecord> BillingUsageRecords { get; }

    DbSet<OnboardingState> OnboardingStates { get; }

    DbSet<OnboardingApplication> OnboardingApplications { get; }

    DbSet<BlueprintImport> BlueprintImports { get; }

    DbSet<PiiRule> PiiRules { get; }

    DbSet<AuditPolicy> AuditPolicies { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
