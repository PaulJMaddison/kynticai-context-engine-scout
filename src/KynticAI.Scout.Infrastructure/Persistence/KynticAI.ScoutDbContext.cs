using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Saas;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.Infrastructure.Persistence;

public sealed class ScoutDbContext(DbContextOptions<ScoutDbContext> options)
    : DbContext(options), IScoutDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<OperatorAccount> OperatorAccounts => Set<OperatorAccount>();

    public DbSet<DataSource> DataSources => Set<DataSource>();

    public DbSet<SemanticAttributeDefinition> SemanticAttributeDefinitions => Set<SemanticAttributeDefinition>();

    public DbSet<SelectorDefinition> SelectorDefinitions => Set<SelectorDefinition>();

    public DbSet<SelectorExecution> SelectorExecutions => Set<SelectorExecution>();

    public DbSet<ContextSnapshot> ContextSnapshots => Set<ContextSnapshot>();

    public DbSet<ContextFact> ContextFacts => Set<ContextFact>();

    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();

    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<RecomputeJob> RecomputeJobs => Set<RecomputeJob>();

    public DbSet<ProvenanceMetadata> ProvenanceMetadata => Set<ProvenanceMetadata>();

    public DbSet<ConnectorCredential> ConnectorCredentials => Set<ConnectorCredential>();

    public DbSet<SourceSystemEvent> SourceSystemEvents => Set<SourceSystemEvent>();

    public DbSet<UserSignal> UserSignals => Set<UserSignal>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();

    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();

    public DbSet<BillingPlan> BillingPlans => Set<BillingPlan>();

    public DbSet<BillingPlanLimit> BillingPlanLimits => Set<BillingPlanLimit>();

    public DbSet<ApiClient> ApiClients => Set<ApiClient>();

    public DbSet<WebhookSigningSecret> WebhookSigningSecrets => Set<WebhookSigningSecret>();

    public DbSet<ConnectorInstallation> ConnectorInstallations => Set<ConnectorInstallation>();

    public DbSet<ConnectorCaptureCheckpoint> ConnectorCaptureCheckpoints => Set<ConnectorCaptureCheckpoint>();

    public DbSet<ConnectorCatalogueEntry> ConnectorCatalogueEntries => Set<ConnectorCatalogueEntry>();

    public DbSet<ContextPackage> ContextPackages => Set<ContextPackage>();

    public DbSet<BillingUsageRecord> BillingUsageRecords => Set<BillingUsageRecord>();

    public DbSet<OnboardingState> OnboardingStates => Set<OnboardingState>();

    public DbSet<OnboardingApplication> OnboardingApplications => Set<OnboardingApplication>();

    public DbSet<BlueprintImport> BlueprintImports => Set<BlueprintImport>();

    public DbSet<PiiRule> PiiRules => Set<PiiRule>();

    public DbSet<AuditPolicy> AuditPolicies => Set<AuditPolicy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ScoutDbContext).Assembly,
            type => type.Namespace == typeof(ScoutDbContext).Namespace);
    }
}
