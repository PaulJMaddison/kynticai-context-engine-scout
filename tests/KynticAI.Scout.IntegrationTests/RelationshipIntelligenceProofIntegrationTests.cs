using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using KynticAI.Scout.Api.Rest;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Domain.Saas;
using KynticAI.Scout.Infrastructure.Auth;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace KynticAI.Scout.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class RelationshipIntelligenceProofIntegrationTests
{
    private static readonly DateTime ProofNow = new(2026, 06, 16, 12, 00, 00, DateTimeKind.Utc);

    [Fact]
    public async Task NextAction_EmailSubject_ReturnsLinkedEvidenceAndCitations()
    {
        await using var factory = new RelationshipIntelligenceWebApplicationFactory();
        await SeedProofDataAsync(factory.Services);
        using var client = factory.CreateClient();
        Authenticate(client, OperatorRole.TenantAdmin);

        var result = await GenerateNextActionAsync(
            client,
            new V1NextActionRequest("demo", "email", "mara.singh@northstar-saas.example", "sale", "customer_outreach", "tenant_admin"));

        Assert.Equal("sale", result["objective"]!.GetValue<string>());
        Assert.False(result["governance"]!["cloudPayloadContainsRawCustomerData"]!.GetValue<bool>());
        AssertLinkedRecord(result, "CustomerContact");
        AssertLinkedRecord(result, "CustomerAccount");
        AssertLinkedRecord(result, "EmailEngagementEvent");
        AssertLinkedRecord(result, "WebConversionEvent");
        AssertLinkedRecord(result, "SupportTicket");
        AssertLinkedRecord(result, "ProductUsageSummary");
        AssertLinkedRecord(result, "BillingMetric");

        Assert.Contains(result["relationships"]!.AsArray(), relationship =>
            relationship?["relationshipType"]?.GetValue<string>() == "EmailToContact"
            && relationship?["linkKind"]?.GetValue<string>() == "deterministic");
        Assert.Contains(result["relationships"]!.AsArray(), relationship =>
            relationship?["relationshipType"]?.GetValue<string>() == "AccountToWebConversion"
            && relationship?["citationIds"]?.AsArray().Count > 0);

        var provenanceIds = result["provenance"]!.AsArray()
            .Select(item => item!["citationId"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);
        var recommendedCitationIds = result["recommendedNextAction"]!["citationIds"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .ToList();

        Assert.NotEmpty(recommendedCitationIds);
        Assert.All(recommendedCitationIds, citationId => Assert.Contains(citationId, provenanceIds));
        Assert.Contains("Northstar Analytics", result["evidencePack"]!["localDerivedEvidencePackageJson"]!.GetValue<string>(), StringComparison.Ordinal);

        var handoffJson = result["evidencePack"]!["enterpriseRelationshipEngineHandoffJson"]!.GetValue<string>();
        var handoff = JsonNode.Parse(handoffJson)!.AsObject();
        Assert.Equal("ucl.enterprise-relationship-engine-handoff", handoff["artifactKind"]!.GetValue<string>());
        Assert.Equal("ucl.enterprise-relationship-engine-handoff.v1", handoff["artifactVersion"]!.GetValue<string>());
        Assert.Equal("BasicRelationshipEngine", handoff["fallbackEngine"]!.GetValue<string>());
        Assert.False(handoff["requiresLiveEnterpriseService"]!.GetValue<bool>());
        Assert.False(handoff["enterpriseOnlyInternalsIncluded"]!.GetValue<bool>());
        Assert.False(handoff["relationshipWeighting"]!["scoutWeightsAreCanonical"]!.GetValue<bool>());
        Assert.Equal("Enterprise", handoff["relationshipWeighting"]!["canonicalOwner"]!.GetValue<string>());
        Assert.NotEmpty(handoff["candidateRelationships"]!.AsArray());
        Assert.Contains(handoff["requiredEnterpriseOutputs"]!.AsArray(), output =>
            output?.GetValue<string>() == "canonicalRelationshipWeights");
    }

    [Fact]
    public async Task NextAction_ReturnsSimilarWonAndLostCohorts()
    {
        await using var factory = new RelationshipIntelligenceWebApplicationFactory();
        await SeedProofDataAsync(factory.Services);
        using var client = factory.CreateClient();
        Authenticate(client, OperatorRole.TenantAdmin);

        var result = await GenerateNextActionAsync(
            client,
            new V1NextActionRequest("demo", "contact", "contact-b2b-saas", "sale", "customer_outreach", "tenant_admin"));

        var patterns = result["similarWonLostPatterns"]!.AsArray();
        Assert.Contains(patterns, pattern =>
            pattern?["outcome"]?.GetValue<string>() == "won"
            && pattern?["similarityScore"]?.GetValue<decimal>() >= 0.45m);
        Assert.Contains(patterns, pattern =>
            pattern?["outcome"]?.GetValue<string>() == "lost"
            && pattern?["similarityScore"]?.GetValue<decimal>() >= 0.45m);
        Assert.Contains(result["relationships"]!.AsArray(), relationship =>
            relationship?["linkKind"]?.GetValue<string>() == "probabilistic"
            && relationship?["relationshipType"]?.GetValue<string>() == "SimilarProductUsagePattern");
    }

    [Fact]
    public async Task NextAction_AllFocusedSyntheticDomains_ReturnExpectedProofShapes()
    {
        await using var factory = new RelationshipIntelligenceWebApplicationFactory();
        await SeedProofDataAsync(factory.Services);
        using var client = factory.CreateClient();
        Authenticate(client, OperatorRole.TenantAdmin);

        foreach (var scenario in FocusedDomainScenarios())
        {
            var result = await GenerateNextActionAsync(
                client,
                new V1NextActionRequest("demo", "email", scenario.SubjectIdentifier, scenario.Objective, scenario.Purpose, "tenant_admin"));

            Assert.Equal(scenario.ExpectedNextBestAction, result["recommendedNextAction"]!["action"]!.GetValue<string>());

            foreach (var recordType in RequiredDomainRecordTypes)
            {
                AssertLinkedRecord(result, recordType);
            }

            foreach (var relationshipType in RequiredDeterministicRelationshipTypes)
            {
                AssertDeterministicRelationship(result, relationshipType);
            }

            AssertSignalScore(result, "pricing-intent", expectPositiveScore: true);
            AssertSignalScore(result, "active-usage", expectPositiveScore: true);
            AssertSignalScore(result, "opportunity-momentum", expectPositiveScore: true);
            AssertSignalScore(result, "support-blockers", scenario.ExpectedBlockerSignals.Contains("support-blockers", StringComparer.Ordinal));
            AssertSignalScore(result, "billing-blockers", scenario.ExpectedBlockerSignals.Contains("billing-blockers", StringComparer.Ordinal));

            Assert.Contains(result["weightedSignals"]!.AsArray(), signal =>
                signal?["direction"]?.GetValue<string>() == "positive"
                && signal?["score"]?.GetValue<decimal>() > 0m);
            Assert.Contains(result["similarWonLostPatterns"]!.AsArray(), pattern =>
                pattern?["outcome"]?.GetValue<string>() == "won"
                && pattern?["similarityScore"]?.GetValue<decimal>() >= 0.45m);
            Assert.Contains(result["similarWonLostPatterns"]!.AsArray(), pattern =>
                pattern?["outcome"]?.GetValue<string>() == "lost"
                && pattern?["similarityScore"]?.GetValue<decimal>() >= 0.45m);

            if (scenario.ExpectStaleCaveat)
            {
                Assert.Contains(result["caveats"]!.AsArray(), caveat =>
                    caveat?.GetValue<string>().Contains("outside the current 30-day decision window", StringComparison.OrdinalIgnoreCase) == true);
            }
        }

        Authenticate(client, OperatorRole.ReadOnly);
        foreach (var scenario in FocusedDomainScenarios())
        {
            var result = await GenerateNextActionAsync(
                client,
                new V1NextActionRequest("demo", "email", scenario.SubjectIdentifier, scenario.Objective, scenario.Purpose, "read_only"));

            Assert.Contains(result["governance"]!["maskedFields"]!.AsArray(), field => field?.GetValue<string>() == "contact.email");
            Assert.Contains(result["governance"]!["maskedFields"]!.AsArray(), field => field?.GetValue<string>() == "account.name");
            Assert.DoesNotContain(scenario.SubjectIdentifier, result["evidencePack"]!["localDerivedEvidencePackageJson"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(scenario.AccountName, result["evidencePack"]!["cloudAggregateUsagePayloadJson"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SyntheticFixtureDocumentsEveryTargetDomain()
    {
        var fixturePath = Path.Combine(FindRepositoryRoot(), "samples", "relationship-intelligence", "exact-data-proof.synthetic.json");
        var fixture = JsonNode.Parse(File.ReadAllText(fixturePath))!.AsObject();
        var domains = fixture["domains"]!.AsArray();
        var domainNames = domains
            .Select(domain => domain!["domain"]!.GetValue<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var scenario in FocusedDomainScenarios())
        {
            Assert.Contains(scenario.Domain, domainNames);
            var domain = domains.Single(item => item!["domain"]!.GetValue<string>().Equals(scenario.Domain, StringComparison.OrdinalIgnoreCase))!.AsObject();
            Assert.Equal(scenario.ExpectedNextBestAction, domain["expectedNextBestAction"]!["action"]!.GetValue<string>());
            Assert.NotEmpty(domain["blockers"]!.AsArray());
            Assert.NotEmpty(domain["positiveSignals"]!.AsArray());
            Assert.Contains(domain["expectedSimilarOutcomes"]!.AsArray(), outcome => outcome?["outcome"]?.GetValue<string>() == "won");
            Assert.Contains(domain["expectedSimilarOutcomes"]!.AsArray(), outcome => outcome?["outcome"]?.GetValue<string>() == "lost");
            Assert.NotEmpty(domain["governanceExamples"]!.AsArray());
        }

        Assert.True(fixture["dataPolicy"]!["containsCustomerData"]!.GetValue<bool>() is false);
        Assert.True(fixture["dataPolicy"]!["containsPatientData"]!.GetValue<bool>() is false);
        Assert.True(fixture["dataPolicy"]!["containsCandidateData"]!.GetValue<bool>() is false);
        Assert.True(fixture["dataPolicy"]!["containsFinancialAccountData"]!.GetValue<bool>() is false);
    }

    [Fact]
    public async Task NextAction_ReadOnlyGovernance_MasksEvidenceAndCloudProjection()
    {
        await using var factory = new RelationshipIntelligenceWebApplicationFactory();
        await SeedProofDataAsync(factory.Services);
        using var client = factory.CreateClient();
        Authenticate(client, OperatorRole.ReadOnly);

        var result = await GenerateNextActionAsync(
            client,
            new V1NextActionRequest("demo", "email", "mara.singh@northstar-saas.example", "sale", "customer_outreach", "read_only"));

        var contact = result["exactLinkedRecords"]!["records"]!.AsArray()
            .Single(record => record!["recordType"]!.GetValue<string>() == "CustomerContact"
                && record["externalId"]!.GetValue<string>() == "contact-b2b-saas");
        Assert.Equal("m***@northstar-saas.example", contact!["fields"]!["email"]!.GetValue<string>());
        Assert.Contains(result["governance"]!["maskedFields"]!.AsArray(), field => field?.GetValue<string>() == "contact.email");
        Assert.Contains(result["governance"]!["maskedFields"]!.AsArray(), field => field?.GetValue<string>() == "account.name");

        var localPackage = result["evidencePack"]!["localDerivedEvidencePackageJson"]!.GetValue<string>();
        var cloudPackage = result["evidencePack"]!["cloudAggregateUsagePayloadJson"]!.GetValue<string>();
        var cloudJson = JsonNode.Parse(cloudPackage)!.AsObject();
        Assert.DoesNotContain("mara.singh@northstar-saas.example", localPackage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mara Singh", localPackage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mara.singh@northstar-saas.example", cloudPackage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Northstar Analytics", cloudPackage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"records\"", cloudPackage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"relationshipTypes\"", cloudPackage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"weightedSignals\"", cloudPackage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"citationIds\"", cloudPackage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(cloudJson["relationshipTypes"]);
        Assert.Null(cloudJson["weightedSignals"]);
        Assert.Null(cloudJson["recommendation"]);
        Assert.Null(cloudJson["citationIds"]);
    }

    [Fact]
    public async Task NextAction_StaleConflictingAndInsufficientData_ReturnCaveats()
    {
        await using var factory = new RelationshipIntelligenceWebApplicationFactory();
        await SeedProofDataAsync(factory.Services);
        using var client = factory.CreateClient();
        Authenticate(client, OperatorRole.TenantAdmin);

        var stale = await GenerateNextActionAsync(
            client,
            new V1NextActionRequest("demo", "email", "elena.morris@hirelane-recruiting.example", "sale", "customer_outreach", "tenant_admin"));
        Assert.Contains(stale["caveats"]!.AsArray(), caveat =>
            caveat?.GetValue<string>().Contains("outside the current 30-day decision window", StringComparison.OrdinalIgnoreCase) == true);

        var conflicting = await GenerateNextActionAsync(
            client,
            new V1NextActionRequest("demo", "email", "oscar.reed@signalbridge-support.example", "churn", "retention", "tenant_admin"));
        Assert.Contains(conflicting["caveats"]!.AsArray(), caveat =>
            caveat?.GetValue<string>().Contains("Conflicting exact data", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(conflicting["weightedSignals"]!.AsArray(), signal =>
            signal?["signalKey"]?.GetValue<string>() == "support-blockers"
            && signal?["contribution"]?.GetValue<decimal>() < 0m);

        var insufficient = await GenerateNextActionAsync(
            client,
            new V1NextActionRequest("demo", "email", "iris.chen@clinic-lite-ops.example", "support", "support_followup", "tenant_admin"));
        Assert.Contains(insufficient["caveats"]!.AsArray(), caveat =>
            caveat?.GetValue<string>().Contains("Insufficient exact operational evidence", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(insufficient["caveats"]!.AsArray(), caveat =>
            caveat?.GetValue<string>().Contains("No linked email engagement events", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task NextAction_ScaleProof_HandlesThousandsOfSyntheticOperationalRecords()
    {
        await using var factory = new RelationshipIntelligenceWebApplicationFactory();
        await SeedProofDataAsync(factory.Services, includeScale: true);
        using var client = factory.CreateClient();
        Authenticate(client, OperatorRole.TenantAdmin);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CustomerOpsDbContext>();
            Assert.True(await dbContext.CustomerAccounts.CountAsync() >= 1_000);
            Assert.True(await dbContext.CustomerContacts.CountAsync() >= 1_000);
            Assert.True(await dbContext.CustomerUsers.CountAsync() >= 1_000);
            var eventCount = await dbContext.EmailEngagementEvents.CountAsync()
                + await dbContext.WebConversionEvents.CountAsync()
                + await dbContext.ProductUsageSummaries.CountAsync()
                + await dbContext.BillingMetrics.CountAsync()
                + await dbContext.SalesOpportunities.CountAsync();
            Assert.True(eventCount >= 5_000);
        }

        var result = await GenerateNextActionAsync(
            client,
            new V1NextActionRequest("demo", "email", "mara.singh@northstar-saas.example", "sale", "customer_outreach", "tenant_admin"));

        Assert.True(result["relationships"]!.AsArray().Count >= 10);
        Assert.True(result["similarWonLostPatterns"]!.AsArray().Count >= 2);
        Assert.True(result["exactLinkedRecords"]!["records"]!.AsArray().Count < 100);
        Assert.DoesNotContain("mara.singh", result["evidencePack"]!["cloudAggregateUsagePayloadJson"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonObject> GenerateNextActionAsync(HttpClient client, V1NextActionRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/intelligence/next-action", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }

    private static void AssertLinkedRecord(JsonObject result, string recordType)
    {
        Assert.Contains(result["exactLinkedRecords"]!["records"]!.AsArray(), record =>
            record?["recordType"]?.GetValue<string>() == recordType
            && record?["citationId"]?.GetValue<string>().StartsWith("EVID-", StringComparison.Ordinal) == true);
    }

    private static void AssertDeterministicRelationship(JsonObject result, string relationshipType)
    {
        Assert.Contains(result["relationships"]!.AsArray(), relationship =>
            relationship?["relationshipType"]?.GetValue<string>() == relationshipType
            && relationship?["linkKind"]?.GetValue<string>() == "deterministic"
            && relationship?["citationIds"]?.AsArray().Count > 0);
    }

    private static void AssertSignalScore(JsonObject result, string signalKey, bool expectPositiveScore)
    {
        var signal = result["weightedSignals"]!.AsArray()
            .Single(item => item!["signalKey"]!.GetValue<string>() == signalKey)!
            .AsObject();
        var score = signal["score"]!.GetValue<decimal>();

        if (expectPositiveScore)
        {
            Assert.True(score > 0m, $"Expected {signalKey} to carry a positive score.");
            return;
        }

        Assert.True(score >= 0m, $"Expected {signalKey} to be present as a governed blocker signal.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KynticAI.Scout.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static IReadOnlyList<DomainProofScenario> FocusedDomainScenarios() =>
    [
        new(
            "B2B SaaS",
            "mara.singh@northstar-saas.example",
            "Northstar Analytics",
            "sale",
            "customer_outreach",
            "Send a focused executive follow-up and propose a pricing or implementation call",
            [],
            ExpectStaleCaveat: false),
        new(
            "Ecommerce",
            "leah.brooks@cartwheel-commerce.example",
            "Cartwheel Commerce",
            "conversion",
            "conversion_recovery",
            "Send a focused executive follow-up and propose a pricing or implementation call",
            [],
            ExpectStaleCaveat: false),
        new(
            "Support churn",
            "oscar.reed@signalbridge-support.example",
            "SignalBridge Support",
            "churn",
            "retention",
            "Resolve commercial blockers before asking for expansion",
            ["support-blockers", "billing-blockers"],
            ExpectStaleCaveat: false),
        new(
            "Recruitment",
            "elena.morris@hirelane-recruiting.example",
            "Hirelane Recruiting",
            "sale",
            "customer_outreach",
            "Resolve commercial blockers before asking for expansion",
            ["support-blockers"],
            ExpectStaleCaveat: true),
        new(
            "Finance retention",
            "calvin.ito@redwood-finance.example",
            "Redwood Finance Trust",
            "retention",
            "retention",
            "Resolve commercial blockers before asking for expansion",
            ["support-blockers", "billing-blockers"],
            ExpectStaleCaveat: false),
        new(
            "Healthcare operations",
            "nina.patel@northern-clinic-ops.example",
            "Northern Clinic Operations",
            "support",
            "support_followup",
            "Prioritise the linked support blocker and send a status update",
            ["support-blockers"],
            ExpectStaleCaveat: false)
    ];

    private static readonly string[] RequiredDomainRecordTypes =
    [
        "CustomerAccount",
        "CustomerContact",
        "SalesOpportunity",
        "SalesActivity",
        "EmailEngagementEvent",
        "WebConversionEvent",
        "SupportTicket",
        "ProductUsageSummary",
        "BillingMetric"
    ];

    private static readonly string[] RequiredDeterministicRelationshipTypes =
    [
        "EmailToContact",
        "ContactToAccount",
        "AccountToOpportunity",
        "ContactToEmailEngagement",
        "AccountToWebConversion",
        "AccountToSupportTicket",
        "AccountToProductUsage",
        "AccountToBilling"
    ];

    private static async Task SeedProofDataAsync(IServiceProvider services, bool includeScale = false)
    {
        await using var scope = services.CreateAsyncScope();
        var scoutDbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        var customerOpsDbContext = scope.ServiceProvider.GetRequiredService<CustomerOpsDbContext>();

        var tenant = Tenant.Create("demo", "Relationship Intelligence Proof Tenant", ProofNow);
        SetId(tenant, SeedIds.TenantId);
        var workspace = Workspace.Create(tenant.Id, "primary", "Primary", "Proof workspace", true, ProofNow);
        SetId(workspace, SeedIds.WorkspaceId);
        var admin = OperatorAccount.Create(tenant.Id, "admin@example.test", "Demo Admin", "not-used", OperatorRole.TenantAdmin, ProofNow);
        SetId(admin, SeedIds.AdminId);

        scoutDbContext.Tenants.Add(tenant);
        scoutDbContext.Workspaces.Add(workspace);
        scoutDbContext.OperatorAccounts.Add(admin);
        scoutDbContext.WorkspaceMembers.Add(WorkspaceMember.Create(tenant.Id, workspace.Id, admin.Id, WorkspaceMemberRole.Owner, ProofNow));

        var opsTenant = CustomerOpsTenant.Create("demo", "Relationship Intelligence Proof Tenant", ProofNow);
        customerOpsDbContext.CustomerOpsTenants.Add(opsTenant);

        SeedFocusedProofDomains(customerOpsDbContext, opsTenant.Id);
        if (includeScale)
        {
            SeedScaleDomain(customerOpsDbContext, opsTenant.Id);
        }

        await scoutDbContext.SaveChangesAsync();
        await customerOpsDbContext.SaveChangesAsync();
    }

    private static void SeedFocusedProofDomains(CustomerOpsDbContext dbContext, Guid tenantId)
    {
        var b2b = AddAccountAndContact(
            dbContext,
            tenantId,
            "acct-b2b-saas",
            "Northstar Analytics",
            "northstar-saas.example",
            "B2B SaaS",
            "enterprise",
            "evaluation",
            "contact-b2b-saas",
            "user-b2b-saas",
            "Mara Singh",
            "mara.singh@northstar-saas.example",
            "VP Revenue Operations",
            "vp",
            "Revenue Operations",
            true);
        AddJourneySignals(dbContext, tenantId, b2b, ProofNow, "b2b-saas", "proposal", 84, true, 25, 88, 8, 3, 0, 0, 0, 0, 8_400m);
        AddResolvedSupportTicket(dbContext, tenantId, b2b, "ticket-b2b-saas-resolved", "onboarding", "Implementation security review resolved.", ProofNow.AddDays(-9), ProofNow.AddDays(-6));

        var b2bWon = AddAccountAndContact(
            dbContext,
            tenantId,
            "acct-b2b-won",
            "Orbit SaaS",
            "orbit-saas.example",
            "B2B SaaS",
            "enterprise",
            "customer",
            "contact-b2b-won",
            "user-b2b-won",
            "Jonah Mercer",
            "jonah.mercer@orbit-saas.example",
            "VP Revenue Operations",
            "vp",
            "Revenue Operations",
            true);
        AddJourneySignals(dbContext, tenantId, b2bWon, ProofNow, "b2b-won", "closed_won", 100, false, 24, 87, 7, 3, 0, 0, 0, 0, 8_100m);

        var b2bLost = AddAccountAndContact(
            dbContext,
            tenantId,
            "acct-b2b-lost",
            "Fallow Metrics",
            "fallow-metrics.example",
            "B2B SaaS",
            "enterprise",
            "closed",
            "contact-b2b-lost",
            "user-b2b-lost",
            "Rhea Stone",
            "rhea.stone@fallow-metrics.example",
            "VP Revenue Operations",
            "vp",
            "Revenue Operations",
            true);
        AddJourneySignals(dbContext, tenantId, b2bLost, ProofNow, "b2b-lost", "closed_lost", 0, false, 23, 83, 7, 2, 0, 0, 0, 0, 7_900m);

        var ecommerce = AddAccountAndContact(
            dbContext,
            tenantId,
            "acct-ecommerce",
            "Cartwheel Commerce",
            "cartwheel-commerce.example",
            "Ecommerce",
            "mid-market",
            "trial",
            "contact-ecommerce",
            "user-ecommerce",
            "Leah Brooks",
            "leah.brooks@cartwheel-commerce.example",
            "Head of Growth",
            "director",
            "Marketing",
            true);
        AddJourneySignals(dbContext, tenantId, ecommerce, ProofNow, "ecommerce", "negotiation", 76, true, 20, 76, 10, 2, 0, 0, 0, 0, 4_200m);
        AddResolvedSupportTicket(dbContext, tenantId, ecommerce, "ticket-ecommerce-resolved", "tracking", "Checkout event mapping confirmed.", ProofNow.AddDays(-8), ProofNow.AddDays(-5));
        AddOutcomeCohort(dbContext, tenantId, "ecommerce-won", "Hearth Cart Co", "hearth-cart.example", "Ecommerce", "mid-market", "customer", "Maya Kent", "maya.kent@hearth-cart.example", "Head of Growth", "director", "Marketing", "closed_won", 100, 21, 78, 9, 2, 0, 0, 0, 0, 4_300m);
        AddOutcomeCohort(dbContext, tenantId, "ecommerce-lost", "Basket Drift", "basket-drift.example", "Ecommerce", "mid-market", "closed", "Ollie Byrne", "ollie.byrne@basket-drift.example", "Head of Growth", "director", "Marketing", "closed_lost", 0, 16, 52, 4, 1, 1, 0, 0, 0, 3_700m);

        var supportChurn = AddAccountAndContact(
            dbContext,
            tenantId,
            "acct-support-churn",
            "SignalBridge Support",
            "signalbridge-support.example",
            "Support Operations",
            "mid-market",
            "renewal",
            "contact-support-churn",
            "user-support-churn",
            "Oscar Reed",
            "oscar.reed@signalbridge-support.example",
            "Director of Customer Support",
            "director",
            "Support",
            true);
        AddJourneySignals(dbContext, tenantId, supportChurn, ProofNow, "support-churn", "negotiation", 79, true, 22, 79, 6, 2, 3, 1, 18, 2, 5_600m);
        AddOutcomeCohort(dbContext, tenantId, "support-churn-won", "Beacon Desk", "beacon-desk.example", "Support Operations", "mid-market", "customer", "Rosa Grant", "rosa.grant@beacon-desk.example", "Director of Customer Support", "director", "Support", "closed_won", 100, 23, 80, 6, 2, 1, 0, 0, 0, 5_700m);
        AddOutcomeCohort(dbContext, tenantId, "support-churn-lost", "Queuefall Support", "queuefall-support.example", "Support Operations", "mid-market", "closed", "Samir Cole", "samir.cole@queuefall-support.example", "Director of Customer Support", "director", "Support", "closed_lost", 0, 17, 58, 3, 1, 3, 1, 24, 2, 5_200m);

        var recruitment = AddAccountAndContact(
            dbContext,
            tenantId,
            "acct-recruitment-stale",
            "Hirelane Recruiting",
            "hirelane-recruiting.example",
            "Recruitment",
            "mid-market",
            "evaluation",
            "contact-recruitment-stale",
            "user-recruitment-stale",
            "Elena Morris",
            "elena.morris@hirelane-recruiting.example",
            "VP Talent Operations",
            "vp",
            "Talent",
            true);
        AddJourneySignals(dbContext, tenantId, recruitment, ProofNow.AddDays(-75), "recruitment-stale", "proposal", 72, true, 19, 71, 5, 1, 1, 1, 0, 0, 3_900m);
        AddOutcomeCohort(dbContext, tenantId, "recruitment-won", "TalentLoop Search", "talentloop-search.example", "Recruitment", "mid-market", "customer", "Helena Frost", "helena.frost@talentloop-search.example", "VP Talent Operations", "vp", "Talent", "closed_won", 100, 20, 73, 5, 1, 0, 0, 0, 0, 4_000m);
        AddOutcomeCohort(dbContext, tenantId, "recruitment-lost", "Pipeline Drift Hiring", "pipeline-drift-hiring.example", "Recruitment", "mid-market", "closed", "Theo Park", "theo.park@pipeline-drift-hiring.example", "VP Talent Operations", "vp", "Talent", "closed_lost", 0, 12, 48, 2, 0, 1, 1, 0, 0, 3_600m);

        var finance = AddAccountAndContact(
            dbContext,
            tenantId,
            "acct-finance-retention",
            "Redwood Finance Trust",
            "redwood-finance.example",
            "Financial Services",
            "enterprise",
            "renewal",
            "contact-finance-retention",
            "user-finance-retention",
            "Calvin Ito",
            "calvin.ito@redwood-finance.example",
            "Head of Client Retention",
            "director",
            "Finance",
            true);
        AddJourneySignals(dbContext, tenantId, finance, ProofNow, "finance-retention", "renewal", 64, true, 26, 81, 4, 1, 1, 0, 14, 1, 12_500m);
        AddOutcomeCohort(dbContext, tenantId, "finance-retention-won", "Cedar Ledger Bank", "cedar-ledger.example", "Financial Services", "enterprise", "customer", "Priya Nair", "priya.nair@cedar-ledger.example", "Head of Client Retention", "director", "Finance", "closed_won", 100, 27, 82, 4, 1, 0, 0, 0, 0, 12_800m);
        AddOutcomeCohort(dbContext, tenantId, "finance-retention-lost", "Harbour Arrears Group", "harbour-arrears.example", "Financial Services", "enterprise", "closed", "Martin Vale", "martin.vale@harbour-arrears.example", "Head of Client Retention", "director", "Finance", "closed_lost", 0, 18, 56, 2, 0, 2, 1, 21, 2, 11_900m);

        var healthcare = AddAccountAndContact(
            dbContext,
            tenantId,
            "acct-healthcare-operations",
            "Northern Clinic Operations",
            "northern-clinic-ops.example",
            "Healthcare Operations",
            "mid-market",
            "onboarding",
            "contact-healthcare-operations",
            "user-healthcare-operations",
            "Nina Patel",
            "nina.patel@northern-clinic-ops.example",
            "Operations Lead",
            "manager",
            "Operations",
            false);
        AddJourneySignals(dbContext, tenantId, healthcare, ProofNow, "healthcare-operations", "proposal", 68, true, 17, 69, 3, 1, 2, 1, 0, 0, 6_500m);
        AddOutcomeCohort(dbContext, tenantId, "healthcare-operations-won", "WardFlow Clinics", "wardflow-clinics.example", "Healthcare Operations", "mid-market", "customer", "Anika Rao", "anika.rao@wardflow-clinics.example", "Operations Lead", "manager", "Operations", "closed_won", 100, 18, 70, 3, 1, 1, 1, 0, 0, 6_700m);
        AddOutcomeCohort(dbContext, tenantId, "healthcare-operations-lost", "Clinic Queue Partners", "clinic-queue-partners.example", "Healthcare Operations", "mid-market", "closed", "Ben Walters", "ben.walters@clinic-queue-partners.example", "Operations Lead", "manager", "Operations", "closed_lost", 0, 10, 42, 1, 0, 3, 1, 0, 0, 6_100m);

        AddAccountAndContact(
            dbContext,
            tenantId,
            "acct-healthcare-insufficient",
            "Clinic Lite Operations",
            "clinic-lite-ops.example",
            "Healthcare Operations",
            "mid-market",
            "onboarding",
            "contact-healthcare-insufficient",
            "user-healthcare-insufficient",
            "Iris Chen",
            "iris.chen@clinic-lite-ops.example",
            "Operations Coordinator",
            "manager",
            "Operations",
            false);
    }

    private static ProofSubject AddAccountAndContact(
        CustomerOpsDbContext dbContext,
        Guid tenantId,
        string externalAccountId,
        string accountName,
        string domain,
        string industry,
        string segment,
        string lifecycleStage,
        string externalContactId,
        string externalUserId,
        string fullName,
        string email,
        string jobTitle,
        string seniority,
        string department,
        bool isDecisionMaker)
    {
        var account = CustomerAccount.Create(
            tenantId,
            externalAccountId,
            accountName,
            domain,
            industry,
            segment,
            "EMEA",
            lifecycleStage,
            "Dana Mercer",
            segment == "enterprise" ? 1_500 : 420,
            segment == "enterprise" ? 120_000_000m : 24_000_000m,
            ProofNow);
        var contact = CustomerContact.Create(
            tenantId,
            account.Id,
            externalContactId,
            externalUserId,
            fullName,
            email,
            jobTitle,
            seniority,
            department,
            "email",
            isDecisionMaker,
            ProofNow);
        var user = CustomerUser.Create(
            tenantId,
            account.Id,
            contact.Id,
            externalUserId,
            isDecisionMaker ? "admin" : "member",
            ProofNow.AddDays(-21),
            ProofNow.AddHours(-2),
            lifecycleStage == "trial" || lifecycleStage == "evaluation",
            ProofNow);

        dbContext.CustomerAccounts.Add(account);
        dbContext.CustomerContacts.Add(contact);
        dbContext.CustomerUsers.Add(user);
        return new ProofSubject(account, contact);
    }

    private static void AddOutcomeCohort(
        CustomerOpsDbContext dbContext,
        Guid tenantId,
        string key,
        string accountName,
        string accountDomain,
        string industry,
        string segment,
        string lifecycleStage,
        string contactName,
        string email,
        string jobTitle,
        string seniority,
        string department,
        string opportunityStage,
        int probability,
        int activeDays30,
        int featureAdoptionScore,
        int pricingVisits30d,
        int emailReplies30d,
        int openSupportTickets,
        int severeSupportTickets,
        int daysPastDue,
        int paymentFailures30d,
        decimal monthlyRecurringRevenue)
    {
        var subject = AddAccountAndContact(
            dbContext,
            tenantId,
            $"acct-{key}",
            accountName,
            accountDomain,
            industry,
            segment,
            lifecycleStage,
            $"contact-{key}",
            $"user-{key}",
            contactName,
            email,
            jobTitle,
            seniority,
            department,
            true);
        AddJourneySignals(
            dbContext,
            tenantId,
            subject,
            ProofNow,
            key,
            opportunityStage,
            probability,
            false,
            activeDays30,
            featureAdoptionScore,
            pricingVisits30d,
            emailReplies30d,
            openSupportTickets,
            severeSupportTickets,
            daysPastDue,
            paymentFailures30d,
            monthlyRecurringRevenue);
    }

    private static void AddResolvedSupportTicket(
        CustomerOpsDbContext dbContext,
        Guid tenantId,
        ProofSubject subject,
        string externalTicketId,
        string category,
        string summary,
        DateTime openedAtUtc,
        DateTime resolvedAtUtc)
    {
        dbContext.SupportTickets.Add(SupportTicket.Create(
            tenantId,
            subject.Account.Id,
            subject.Contact.Id,
            externalTicketId,
            "medium",
            "resolved",
            category,
            summary,
            openedAtUtc,
            resolvedAtUtc,
            8,
            ProofNow));
    }

    private static void AddJourneySignals(
        CustomerOpsDbContext dbContext,
        Guid tenantId,
        ProofSubject subject,
        DateTime signalAnchorUtc,
        string key,
        string opportunityStage,
        int probability,
        bool isOpen,
        int activeDays30,
        int featureAdoptionScore,
        int pricingVisits30d,
        int emailReplies30d,
        int openSupportTickets,
        int severeSupportTickets,
        int daysPastDue,
        int paymentFailures30d,
        decimal monthlyRecurringRevenue)
    {
        dbContext.SalesOpportunities.Add(SalesOpportunity.Create(
            tenantId,
            subject.Account.Id,
            subject.Contact.Id,
            $"opp-{key}",
            $"{subject.Account.Name} relationship intelligence proof",
            opportunityStage,
            monthlyRecurringRevenue * 10,
            probability,
            isOpen ? signalAnchorUtc.AddDays(18) : signalAnchorUtc.AddDays(-14),
            isOpen ? "expansion" : "new-business",
            isOpen,
            ProofNow));
        dbContext.SalesActivities.Add(SalesActivity.Create(
            tenantId,
            subject.Account.Id,
            subject.Contact.Id,
            "meeting",
            "outbound",
            probability >= 70 ? "positive_reply" : "follow_up_needed",
            probability >= 70
                ? "Champion asked for implementation and commercial next steps."
                : "Rep noted that more qualification is required.",
            signalAnchorUtc.AddDays(-2),
            ProofNow));

        for (var index = 0; index < emailReplies30d; index++)
        {
            dbContext.EmailEngagementEvents.Add(EmailEngagementEvent.Create(
                tenantId,
                subject.Contact.Id,
                "Relationship Intelligence Proof",
                index == 0 && probability >= 75 ? "meeting_booked" : "reply",
                "email",
                "{}",
                signalAnchorUtc.AddDays(-index - 1),
                ProofNow));
        }

        for (var index = 0; index < pricingVisits30d; index++)
        {
            dbContext.WebConversionEvents.Add(WebConversionEvent.Create(
                tenantId,
                subject.Account.Id,
                subject.Contact.Id,
                index % 4 == 0 ? "trial_activated" : "pricing_viewed",
                index % 4 == 0 ? "trial-start" : "pricing",
                "exact-data-proof",
                index % 2 == 0 ? "email" : "organic",
                70m + index,
                signalAnchorUtc.AddDays(-index - 1),
                ProofNow));
        }

        for (var index = 0; index < openSupportTickets; index++)
        {
            dbContext.SupportTickets.Add(SupportTicket.Create(
                tenantId,
                subject.Account.Id,
                subject.Contact.Id,
                $"ticket-{key}-{index}",
                index < severeSupportTickets ? "critical" : "medium",
                "open",
                "operations",
                "Synthetic blocker for local relationship intelligence proof.",
                signalAnchorUtc.AddDays(-index - 3),
                null,
                null,
                ProofNow));
        }

        dbContext.ProductUsageSummaries.Add(ProductUsageSummary.Create(
            tenantId,
            subject.Account.Id,
            subject.Contact.Id,
            signalAnchorUtc.Date,
            activeDays30,
            Math.Max(4, activeDays30 - 4),
            Math.Max(8, featureAdoptionScore / 2),
            pricingVisits30d,
            featureAdoptionScore,
            42,
            50,
            featureAdoptionScore,
            ProofNow));
        dbContext.BillingMetrics.Add(BillingMetric.Create(
            tenantId,
            subject.Account.Id,
            signalAnchorUtc.Date,
            monthlyRecurringRevenue,
            monthlyRecurringRevenue * 12,
            daysPastDue,
            paymentFailures30d,
            probability / 8,
            daysPastDue > 0 || paymentFailures30d > 0 ? "watch" : "healthy",
            ProofNow));
    }

    private static void SeedScaleDomain(CustomerOpsDbContext dbContext, Guid tenantId)
    {
        for (var index = 0; index < 1_100; index++)
        {
            var account = CustomerAccount.Create(
                tenantId,
                $"acct-scale-{index:0000}",
                $"Scale Proof Account {index:0000}",
                $"scale-{index:0000}.example",
                index % 2 == 0 ? "Ecommerce" : "B2B SaaS",
                index % 3 == 0 ? "enterprise" : "mid-market",
                "Synthetic",
                index % 4 == 0 ? "renewal" : "evaluation",
                "Scale Owner",
                100 + index,
                10_000_000m + index,
                ProofNow);
            var contact = CustomerContact.Create(
                tenantId,
                account.Id,
                $"contact-scale-{index:0000}",
                $"user-scale-{index:0000}",
                $"Scale Contact {index:0000}",
                $"scale.contact.{index:0000}@scale-{index:0000}.example",
                index % 2 == 0 ? "Director of Operations" : "VP Revenue",
                index % 2 == 0 ? "director" : "vp",
                index % 2 == 0 ? "Operations" : "Revenue Operations",
                "email",
                index % 5 != 0,
                ProofNow);
            dbContext.CustomerAccounts.Add(account);
            dbContext.CustomerContacts.Add(contact);
            dbContext.CustomerUsers.Add(CustomerUser.Create(
                tenantId,
                account.Id,
                contact.Id,
                contact.ExternalUserId,
                "member",
                ProofNow.AddDays(-30),
                ProofNow.AddMinutes(-index % 180),
                index % 4 == 0,
                ProofNow));

            dbContext.SalesOpportunities.Add(SalesOpportunity.Create(
                tenantId,
                account.Id,
                contact.Id,
                $"opp-scale-{index:0000}",
                $"Scale outcome {index:0000}",
                index % 3 == 0 ? "closed_won" : index % 3 == 1 ? "closed_lost" : "proposal",
                25_000m + index,
                index % 3 == 0 ? 100 : index % 3 == 1 ? 0 : 55,
                ProofNow.AddDays(index % 3 == 2 ? 16 : -20),
                "new-business",
                index % 3 == 2,
                ProofNow));

            for (var eventIndex = 0; eventIndex < 2; eventIndex++)
            {
                dbContext.EmailEngagementEvents.Add(EmailEngagementEvent.Create(
                    tenantId,
                    contact.Id,
                    "Scale Proof Sequence",
                    eventIndex == 0 ? "open" : "reply",
                    "email",
                    "{}",
                    ProofNow.AddDays(-eventIndex - 1),
                    ProofNow));
                dbContext.WebConversionEvents.Add(WebConversionEvent.Create(
                    tenantId,
                    account.Id,
                    contact.Id,
                    "pricing_viewed",
                    "pricing",
                    "scale-proof",
                    "email",
                    40m + eventIndex,
                    ProofNow.AddDays(-eventIndex - 2),
                    ProofNow));
            }

            dbContext.ProductUsageSummaries.Add(ProductUsageSummary.Create(
                tenantId,
                account.Id,
                contact.Id,
                ProofNow.Date,
                8 + index % 20,
                5 + index % 12,
                10 + index % 35,
                2 + index % 7,
                20 + index % 80,
                10 + index % 40,
                50,
                35 + index % 55,
                ProofNow));
            dbContext.BillingMetrics.Add(BillingMetric.Create(
                tenantId,
                account.Id,
                ProofNow.Date,
                500m + index,
                (500m + index) * 12,
                index % 17 == 0 ? 9 : 0,
                index % 23 == 0 ? 1 : 0,
                index % 12,
                index % 17 == 0 ? "watch" : "healthy",
                ProofNow));
        }
    }

    private static void Authenticate(HttpClient client, OperatorRole role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("relationship-proof-signing-key-1234567890"));
        var token = new JwtSecurityToken(
            issuer: "KynticAI.Scout.RelationshipProofTests",
            audience: "KynticAI.Scout.RelationshipProofTests",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, SeedIds.AdminId.ToString("D")),
                new Claim(ClaimTypes.NameIdentifier, SeedIds.AdminId.ToString("D")),
                new Claim("tenant_id", SeedIds.TenantId.ToString("D")),
                new Claim("tenant_slug", "demo"),
                new Claim("workspace_id", SeedIds.WorkspaceId.ToString("D")),
                new Claim("workspace_slug", "primary"),
                new Claim("display_name", "Proof Operator"),
                new Claim(ClaimTypes.Email, "proof.operator@example.test"),
                new Claim(ClaimTypes.Role, RoleNames.ToClaimValue(role))
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }

    private static void SetId<T>(T entity, Guid id)
        where T : class
        => typeof(T).BaseType!.GetProperty("Id")!.SetValue(entity, id);

    private sealed record ProofSubject(CustomerAccount Account, CustomerContact Contact);

    private sealed record DomainProofScenario(
        string Domain,
        string SubjectIdentifier,
        string AccountName,
        string Objective,
        string Purpose,
        string ExpectedNextBestAction,
        IReadOnlyList<string> ExpectedBlockerSignals,
        bool ExpectStaleCaveat);

    private static class SeedIds
    {
        public static readonly Guid TenantId = Guid.Parse("81111111-1111-1111-1111-111111111111");
        public static readonly Guid WorkspaceId = Guid.Parse("82222222-2222-2222-2222-222222222222");
        public static readonly Guid AdminId = Guid.Parse("83333333-3333-3333-3333-333333333333");
    }

    private sealed class RelationshipIntelligenceWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot databaseRoot = new();
        private readonly string databaseName = $"relationship-intelligence-proof-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["Platform:Mode"] = "BackendOnly",
                    ["Platform:EnableRest"] = "true",
                    ["Platform:EnableGraphQl"] = "true",
                    ["Platform:EnableOpenApi"] = "true",
                    ["Bootstrap:ApplyMigrationsOnStartup"] = "false",
                    ["Bootstrap:SeedDemoData"] = "false",
                    ["Auth:Issuer"] = "KynticAI.Scout.RelationshipProofTests",
                    ["Auth:Audience"] = "KynticAI.Scout.RelationshipProofTests",
                    ["Auth:SigningKey"] = "relationship-proof-signing-key-1234567890",
                    ["Telemetry:OtlpEndpoint"] = string.Empty
                };

                config.AddInMemoryCollection(settings);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ScoutDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ScoutDbContext>>();
                services.RemoveAll<ScoutDbContext>();
                services.RemoveAll<DbContextOptions<CustomerOpsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CustomerOpsDbContext>>();
                services.RemoveAll<CustomerOpsDbContext>();
                services.RemoveAll<KynticAI.Scout.Application.Abstractions.IClock>();

                services.AddDbContext<ScoutDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName, databaseRoot));
                services.AddDbContext<CustomerOpsDbContext>(options =>
                    options.UseInMemoryDatabase($"{databaseName}-ops", databaseRoot));
                services.AddScoped<KynticAI.Scout.Application.Abstractions.IScoutDbContext>(provider =>
                    provider.GetRequiredService<ScoutDbContext>());
                services.AddScoped<KynticAI.Scout.Application.Abstractions.ICustomerOpsDbContext>(provider =>
                    provider.GetRequiredService<CustomerOpsDbContext>());
                services.AddSingleton<KynticAI.Scout.Application.Abstractions.IClock>(new TestClock(ProofNow));

                // Production registers the disabled NextAction service (Scout core does not own
                // sales next-action heuristics). These Proof tests exercise the reference
                // implementation against the public API, so swap in the real reference service.
                services.RemoveAll<INextActionIntelligenceService>();
                services.AddScoped<INextActionIntelligenceService, NextActionIntelligenceService>();

                TestSeedHelper.UseFastPasswordHashing(services);
            });
        }
    }

    private sealed class TestClock(DateTime utcNow) : KynticAI.Scout.Application.Abstractions.IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
