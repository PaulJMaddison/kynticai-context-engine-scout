using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Saas;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.Infrastructure.Persistence;

public sealed class ScoutDbContext(DbContextOptions<ScoutDbContext> options)
    : DbContext(options), IScoutDbContext
{
    private static readonly TimeSpan CaptureLeaseRenewalDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CaptureLeaseRenewalThreshold = TimeSpan.FromMinutes(2);

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

    /// <summary>
    /// Byte-preserving customer-local evidence for connector capture rows. This is separate from
    /// SourceSystemEvent.PayloadJson because PostgreSQL jsonb is semantic storage and may
    /// normalise representation after a round trip.
    /// </summary>
    public DbSet<SourceCapturePayloadEvidence> SourceCapturePayloadEvidenceRecords => Set<SourceCapturePayloadEvidence>();

    /// <summary>
    /// Membership of retained source events in one FULL_SOURCE capture generation. This is what
    /// lets snapshot-only connectors rebuild current state from the latest completed generation
    /// without resurrecting records that existed only in an older snapshot.
    /// </summary>
    public DbSet<SourceCaptureGenerationMember> SourceCaptureGenerationMembers => Set<SourceCaptureGenerationMember>();

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
    public DbSet<ConnectorCaptureOwnership> ConnectorCaptureOwnerships => Set<ConnectorCaptureOwnership>();
    public DbSet<ConnectorCatalogueEntry> ConnectorCatalogueEntries => Set<ConnectorCatalogueEntry>();
    public DbSet<ContextPackage> ContextPackages => Set<ContextPackage>();
    public DbSet<BillingUsageRecord> BillingUsageRecords => Set<BillingUsageRecord>();
    public DbSet<OnboardingState> OnboardingStates => Set<OnboardingState>();
    public DbSet<OnboardingApplication> OnboardingApplications => Set<OnboardingApplication>();
    public DbSet<BlueprintImport> BlueprintImports => Set<BlueprintImport>();
    public DbSet<PiiRule> PiiRules => Set<PiiRule>();
    public DbSet<AuditPolicy> AuditPolicies => Set<AuditPolicy>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareCaptureLeaseRenewals();
        PrepareExactCapturePayloadEvidence();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareCaptureLeaseRenewals();
        PrepareExactCapturePayloadEvidence();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ScoutDbContext).Assembly,
            type => type.Namespace == typeof(ScoutDbContext).Namespace);
    }

    /// <summary>
    /// Whole-source capture can persist thousands of evidence/membership rows inside one leased
    /// batch. Renew a tracked worker lease before any SaveChanges once it enters the final two
    /// minutes. This makes each normal persistence write also prove that the worker still owns a
    /// non-expired lease. If the lease has already expired or another worker has taken it, the
    /// domain/concurrency checks fail closed instead of allowing stale capture to keep writing.
    /// </summary>
    private void PrepareCaptureLeaseRenewals()
    {
        var utcNow = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<ConnectorCaptureCheckpoint>())
        {
            var checkpoint = entry.Entity;
            if (entry.State is EntityState.Detached or EntityState.Deleted
                || string.IsNullOrWhiteSpace(checkpoint.LeaseOwner)
                || !checkpoint.LeaseExpiresAtUtc.HasValue
                || checkpoint.LeaseExpiresAtUtc.Value > utcNow.Add(CaptureLeaseRenewalThreshold))
            {
                continue;
            }

            checkpoint.RenewLease(
                checkpoint.LeaseOwner,
                CaptureLeaseRenewalDuration,
                utcNow);
        }
    }

    /// <summary>
    /// Makes exact replay evidence a persistence invariant instead of a convention individual
    /// connectors can forget. New capture events are verified before the database write, their
    /// local capture envelope is stamped with exact-text.v1, and the exact payload sidecar is
    /// inserted in the same SaveChanges transaction.
    /// </summary>
    private void PrepareExactCapturePayloadEvidence()
    {
        var pendingEvents = ChangeTracker
            .Entries<SourceSystemEvent>()
            .Where(entry => entry.State == EntityState.Added)
            .ToArray();

        foreach (var entry in pendingEvents)
        {
            var sourceEvent = entry.Entity;
            JsonObject? headers;
            try
            {
                headers = JsonNode.Parse(sourceEvent.HeadersJson) as JsonObject;
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    $"Capture event '{sourceEvent.EventId}' has invalid HeadersJson.",
                    exception);
            }

            if (headers?["kynticCapture"] is not JsonObject capture)
            {
                continue;
            }

            var contract = capture["Contract"]?.GetValue<string>();
            if (!string.Equals(contract, LocalDataPlaneContracts.CaptureMetadataV1, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Capture event '{sourceEvent.EventId}' uses unsupported capture metadata contract '{contract ?? "<missing>"}'.");
            }

            var connectorText = capture["ConnectorInstanceId"]?.GetValue<string>();
            if (!Guid.TryParse(connectorText, out var connectorInstallationId)
                || connectorInstallationId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    $"Capture event '{sourceEvent.EventId}' has no valid connector installation id.");
            }

            var coverageScope = capture["CoverageScope"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(coverageScope))
            {
                throw new InvalidOperationException(
                    $"Capture event '{sourceEvent.EventId}' has no coverage scope.");
            }

            var declaredHash = capture["RawPayloadSha256"]?.GetValue<string>();
            if (!IsSha256(declaredHash))
            {
                throw new InvalidOperationException(
                    $"Capture event '{sourceEvent.EventId}' has no valid raw payload SHA-256.");
            }

            var actualHash = Sha256(sourceEvent.PayloadJson);
            if (!string.Equals(actualHash, declaredHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Capture event '{sourceEvent.EventId}' raw payload SHA-256 does not match the exact retained payload text.");
            }

            capture["PayloadStorageContract"] = LocalDataPlaneContracts.PayloadStorageExactTextV1;
            entry.Property(x => x.HeadersJson).CurrentValue = headers.ToJsonString();

            var alreadyTracked = ChangeTracker
                .Entries<SourceCapturePayloadEvidence>()
                .Any(evidenceEntry => evidenceEntry.Entity.SourceSystemEventId == sourceEvent.Id
                    && evidenceEntry.State != EntityState.Deleted);
            if (alreadyTracked)
            {
                continue;
            }

            SourceCapturePayloadEvidenceRecords.Add(
                SourceCapturePayloadEvidence.Create(
                    sourceEvent.TenantId,
                    sourceEvent.Id,
                    connectorInstallationId,
                    LocalDataPlaneContracts.PayloadStorageExactTextV1,
                    coverageScope,
                    sourceEvent.PayloadJson,
                    actualHash,
                    sourceEvent.ReceivedAtUtc));
        }
    }

    private static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
