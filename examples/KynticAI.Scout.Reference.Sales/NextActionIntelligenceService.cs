using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Application.Validation;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.Reference.Sales;

public sealed class NextActionIntelligenceService(
    IScoutDbContext scoutDbContext,
    ICustomerOpsDbContext customerOpsDbContext,
    IClock clock,
    ICurrentActorService currentActorService,
    BasicRelationshipEngine relationshipEngine,
    EnterpriseRelationshipEngineHandoff enterpriseRelationshipEngineHandoff,
    IValidator<NextActionInput> validator)
    : INextActionIntelligenceService
{
    private const string PackageVersion = UclEvidencePackContractVersions.EvidencePackV1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<NextActionResult?> GenerateNextActionAsync(NextActionInput input, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(input, cancellationToken);

        var tenantSlug = NormalizeSlug(input.TenantSlug);
        var tenant = await scoutDbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == tenantSlug, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{tenantSlug}' was not found.");

        var opsTenant = await customerOpsDbContext.CustomerOpsTenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == tenantSlug, cancellationToken);
        if (opsTenant is null)
        {
            return null;
        }

        var actor = currentActorService.GetCurrentActor();
        var effectiveRole = ResolveEffectiveRole(actor, input.ActorRole);
        var governance = GovernanceContext.Create(actor, effectiveRole, input.Purpose);

        var subject = await ResolveSubjectAsync(
            opsTenant.Id,
            input.SubjectType,
            input.SubjectIdentifier,
            cancellationToken);
        if (subject is null)
        {
            return null;
        }

        var exactContext = await LoadExactContextAsync(opsTenant.Id, subject, cancellationToken);
        var citationIds = new CitationIdAllocator();
        var relationshipIds = new RelationshipIdAllocator();
        var records = new List<ExactLinkedRecordSummaryResult>();
        var relationships = new List<RelationshipResult>();
        var provenance = new List<ProvenanceCitationResult>();
        var citationIndex = BuildExactRecordsAndDeterministicRelationships(
            input,
            subject,
            exactContext,
            governance,
            citationIds,
            relationshipIds,
            records,
            relationships,
            provenance);

        var patternContext = await BuildSimilarPatternsAsync(
            opsTenant.Id,
            input,
            subject,
            exactContext,
            governance,
            citationIds,
            relationshipIds,
            relationships,
            provenance,
            cancellationToken);

        var weightedSignals = BuildWeightedSignals(input, subject, exactContext, patternContext.Patterns, citationIndex);
        var score = Clamp01(0.32m + weightedSignals.Sum(x => x.Contribution));
        var confidence = BuildConfidence(score, exactContext, patternContext.Patterns);
        var caveats = BuildCaveats(exactContext, patternContext.Patterns);
        var recommendedAction = BuildRecommendedAction(input, exactContext, weightedSignals, score);
        var draft = BuildDraftResponse(input, subject, governance, recommendedAction);
        var exactSummary = new ExactLinkedRecordsSummaryResult(
            records
                .GroupBy(x => x.RecordType, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase),
            records);

        var generatedAtUtc = clock.UtcNow;
        var packageId = $"EP-{Guid.NewGuid():N}";
        var sortedMaskedFields = governance.MaskedFields.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var sortedDeniedFields = governance.DeniedFields.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var localEvidencePack = new UclEvidencePackV1(
            UclEvidencePackContractVersions.EvidencePackKind,
            PackageVersion,
            packageId,
            generatedAtUtc,
            tenantSlug,
            UclEvidencePackContractVersions.CustomerOwnedDataPlane,
            new UclEvidenceSubjectV1(
                NormalizeSubjectType(input.SubjectType),
                governance.MaskSubjectIdentifier(input.SubjectType, input.SubjectIdentifier),
                subject.Account.ExternalAccountId,
                subject.PrimaryContact?.ExternalContactId),
            NormalizeObjective(input.Objective),
            input.Purpose.Trim(),
            RoleToContractValue(effectiveRole),
            exactSummary,
            relationships,
            patternContext.Patterns,
            weightedSignals,
            relationshipEngine.BuildWeightingContract(),
            recommendedAction,
            draft,
            confidence,
            caveats,
            provenance,
            new UclEvidenceGovernanceV1(
                governance.AppliedRules,
                sortedMaskedFields,
                sortedDeniedFields,
                RawDataRetainedInCustomerDataPlane: true));
        var localEvidenceValidation = UclEvidencePackV1Validator.Validate(localEvidencePack);
        if (!localEvidenceValidation.IsValid)
        {
            throw new InvalidOperationException($"UclEvidencePackV1 validation failed: {string.Join("; ", localEvidenceValidation.Errors)}");
        }

        var localDerivedEvidencePackageJson = JsonSerializer.Serialize(localEvidencePack, JsonOptions);
        var enterpriseHandoff = enterpriseRelationshipEngineHandoff.BuildArtifact(localEvidencePack);
        var enterpriseHandoffValidation = UclEnterpriseRelationshipEngineHandoffV1Validator.Validate(enterpriseHandoff);
        if (!enterpriseHandoffValidation.IsValid)
        {
            throw new InvalidOperationException($"UclEnterpriseRelationshipEngineHandoffV1 validation failed: {string.Join("; ", enterpriseHandoffValidation.Errors)}");
        }

        var enterpriseRelationshipEngineHandoffJson = JsonSerializer.Serialize(enterpriseHandoff, JsonOptions);

        var cloudAggregateUsagePayload = BuildCloudAggregateUsagePayload(
            tenantSlug,
            generatedAtUtc,
            governance);
        var cloudAggregateUsagePayloadJson = JsonSerializer.Serialize(cloudAggregateUsagePayload, JsonOptions);
        var cloudPayloadValidation = UclCloudAggregateUsageV1Validator.ValidateJson(cloudAggregateUsagePayloadJson);
        if (!cloudPayloadValidation.IsValid)
        {
            throw new InvalidOperationException($"UclCloudAggregateUsageV1 validation failed: {string.Join("; ", cloudPayloadValidation.Errors)}");
        }

        var governanceResult = new GovernanceDecisionResult(
            IsAllowed: true,
            DataPlane: "customer-owned-data-plane",
            RawDataRetainedInCustomerDataPlane: true,
            CloudPayloadContainsRawCustomerData: false,
            AppliedRules: governance.AppliedRules,
            MaskedFields: sortedMaskedFields,
            DeniedFields: sortedDeniedFields,
            CloudAggregateUsagePayloadJson: cloudAggregateUsagePayloadJson);

        var evidencePack = new EvidencePackResult(
            packageId,
            PackageVersion,
            generatedAtUtc,
            localDerivedEvidencePackageJson,
            cloudAggregateUsagePayloadJson,
            CloudPayloadContainsRawCustomerData: false,
            EnterpriseRelationshipEngineHandoffJson: enterpriseRelationshipEngineHandoffJson);

        var result = new NextActionResult(
            tenantSlug,
            NormalizeSubjectType(input.SubjectType),
            governance.MaskSubjectIdentifier(input.SubjectType, input.SubjectIdentifier),
            NormalizeObjective(input.Objective),
            input.Purpose.Trim(),
            RoleToContractValue(effectiveRole),
            exactSummary,
            relationships,
            patternContext.Patterns,
            weightedSignals,
            recommendedAction,
            draft,
            confidence,
            caveats,
            provenance,
            governanceResult,
            evidencePack);

        scoutDbContext.AuditEvents.Add(AuditEvent.Create(
            tenant.Id,
            actor.Email,
            "intelligence.next-action.generated",
            nameof(EvidencePack),
            packageId,
            Guid.NewGuid().ToString("N"),
            cloudAggregateUsagePayloadJson,
            beforeJson: null,
            afterJson: null,
            clock.UtcNow));
        await scoutDbContext.SaveChangesAsync(cancellationToken);

        return result;
    }

    private async Task<SubjectContext?> ResolveSubjectAsync(
        Guid customerOpsTenantId,
        string subjectType,
        string subjectIdentifier,
        CancellationToken cancellationToken)
    {
        var normalizedSubjectType = NormalizeSubjectType(subjectType);
        var identifier = subjectIdentifier.Trim();
        var normalizedEmail = identifier.ToLowerInvariant();

        if (normalizedSubjectType is "email" or "contact")
        {
            var contact = await customerOpsDbContext.CustomerContacts
                .AsNoTracking()
                .Include(x => x.Account)
                .FirstOrDefaultAsync(
                    x => x.CustomerOpsTenantId == customerOpsTenantId
                        && (normalizedSubjectType == "email"
                            ? x.Email == normalizedEmail
                            : x.ExternalContactId == identifier || x.ExternalUserId == identifier || x.Email == normalizedEmail),
                    cancellationToken);

            return contact is null
                ? null
                : new SubjectContext(
                    normalizedSubjectType,
                    identifier,
                    contact.Account,
                    [contact],
                    contact);
        }

        var normalizedAccount = identifier.ToLowerInvariant();
        var account = await customerOpsDbContext.CustomerAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.CustomerOpsTenantId == customerOpsTenantId
                    && (x.ExternalAccountId == identifier || x.Domain == normalizedAccount),
                cancellationToken);
        if (account is null)
        {
            return null;
        }

        var contacts = await customerOpsDbContext.CustomerContacts
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == customerOpsTenantId && x.CustomerAccountId == account.Id)
            .OrderByDescending(x => x.IsDecisionMaker)
            .ThenBy(x => x.FullName)
            .Take(12)
            .ToListAsync(cancellationToken);

        return new SubjectContext(
            normalizedSubjectType,
            identifier,
            account,
            contacts,
            contacts.FirstOrDefault());
    }

    private async Task<ExactContext> LoadExactContextAsync(
        Guid customerOpsTenantId,
        SubjectContext subject,
        CancellationToken cancellationToken)
    {
        var contactIds = subject.Contacts.Select(x => x.Id).ToHashSet();
        var accountId = subject.Account.Id;
        var cutoff30d = clock.UtcNow.AddDays(-30);

        var opportunities = await customerOpsDbContext.SalesOpportunities
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == customerOpsTenantId && x.CustomerAccountId == accountId)
            .OrderByDescending(x => x.IsOpen)
            .ThenByDescending(x => x.CloseDateUtc)
            .Take(12)
            .ToListAsync(cancellationToken);

        var salesActivities = await customerOpsDbContext.SalesActivities
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == customerOpsTenantId
                && x.CustomerAccountId == accountId
                && (!contactIds.Any() || x.CustomerContactId == null || contactIds.Contains(x.CustomerContactId.Value)))
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(12)
            .ToListAsync(cancellationToken);

        var emailEvents = contactIds.Count == 0
            ? []
            : await customerOpsDbContext.EmailEngagementEvents
                .AsNoTracking()
                .Where(x => x.CustomerOpsTenantId == customerOpsTenantId && contactIds.Contains(x.CustomerContactId))
                .OrderByDescending(x => x.OccurredAtUtc)
                .Take(16)
                .ToListAsync(cancellationToken);

        var webEvents = await customerOpsDbContext.WebConversionEvents
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == customerOpsTenantId
                && x.CustomerAccountId == accountId
                && (!contactIds.Any() || x.CustomerContactId == null || contactIds.Contains(x.CustomerContactId.Value)))
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(16)
            .ToListAsync(cancellationToken);

        var supportTickets = await customerOpsDbContext.SupportTickets
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == customerOpsTenantId
                && x.CustomerAccountId == accountId
                && (!contactIds.Any() || x.CustomerContactId == null || contactIds.Contains(x.CustomerContactId.Value)))
            .OrderByDescending(x => x.OpenedAtUtc)
            .Take(12)
            .ToListAsync(cancellationToken);

        var usageSummaries = await customerOpsDbContext.ProductUsageSummaries
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == customerOpsTenantId
                && x.CustomerAccountId == accountId
                && (!contactIds.Any() || x.CustomerContactId == null || contactIds.Contains(x.CustomerContactId.Value)))
            .OrderByDescending(x => x.SummaryDateUtc)
            .Take(12)
            .ToListAsync(cancellationToken);

        var billingMetrics = await customerOpsDbContext.BillingMetrics
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == customerOpsTenantId && x.CustomerAccountId == accountId)
            .OrderByDescending(x => x.MetricDateUtc)
            .Take(6)
            .ToListAsync(cancellationToken);

        return new ExactContext(
            opportunities,
            salesActivities,
            emailEvents,
            webEvents,
            supportTickets,
            usageSummaries,
            billingMetrics,
            cutoff30d);
    }

    private CitationIndex BuildExactRecordsAndDeterministicRelationships(
        NextActionInput input,
        SubjectContext subject,
        ExactContext exactContext,
        GovernanceContext governance,
        CitationIdAllocator citationIds,
        RelationshipIdAllocator relationshipIds,
        List<ExactLinkedRecordSummaryResult> records,
        List<RelationshipResult> relationships,
        List<ProvenanceCitationResult> provenance)
    {
        var citationIndex = new CitationIndex();
        var accountCitation = AddRecord(
            records,
            provenance,
            citationIds,
            "CustomerAccount",
            subject.Account.Id,
            subject.Account.ExternalAccountId,
            governance.AccountLabel(subject.Account),
            $"Account {governance.AccountLabel(subject.Account)} in {subject.Account.Segment} / {subject.Account.Industry}.",
            subject.Account.UpdatedAtUtc,
            governance.HasMaskedBusinessDetails,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalAccountId"] = subject.Account.ExternalAccountId,
                ["name"] = governance.AccountName(subject.Account),
                ["domain"] = governance.AccountDomain(subject.Account),
                ["industry"] = subject.Account.Industry,
                ["segment"] = subject.Account.Segment,
                ["region"] = subject.Account.Region,
                ["lifecycleStage"] = subject.Account.LifecycleStage
            });
        citationIndex.Account = accountCitation.CitationId;

        foreach (var contact in subject.Contacts)
        {
            var contactRecord = AddRecord(
                records,
                provenance,
                citationIds,
                "CustomerContact",
                contact.Id,
                contact.ExternalContactId,
                governance.ContactLabel(contact),
                $"Contact {governance.ContactLabel(contact)} is a {contact.Seniority} stakeholder in {contact.Department}.",
                contact.UpdatedAtUtc,
                governance.HasMaskedPii,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["externalContactId"] = contact.ExternalContactId,
                    ["externalUserId"] = contact.ExternalUserId,
                    ["fullName"] = governance.ContactName(contact),
                    ["email"] = governance.Email(contact.Email, "contact.email"),
                    ["jobTitle"] = contact.JobTitle,
                    ["seniority"] = contact.Seniority,
                    ["department"] = contact.Department,
                    ["preferredChannel"] = contact.PreferredChannel,
                    ["isDecisionMaker"] = contact.IsDecisionMaker.ToString()
                });
            citationIndex.ContactCitations[contact.Id] = contactRecord.CitationId;

            relationships.Add(BuildRelationship(
                relationshipIds,
                RelationshipType.EmailToContact,
                "deterministic",
                "email",
                HashValue(contact.Email),
                "CustomerContact",
                contact.Id.ToString("D"),
                1.0m,
                input.Objective,
                "Normalised email address resolved to this contact.",
                [contactRecord.CitationId]));
            relationships.Add(BuildRelationship(
                relationshipIds,
                RelationshipType.ContactToAccount,
                "deterministic",
                "CustomerContact",
                contact.Id.ToString("D"),
                "CustomerAccount",
                subject.Account.Id.ToString("D"),
                1.0m,
                input.Objective,
                "Contact carries a customer account foreign key.",
                [contactRecord.CitationId, accountCitation.CitationId]));
        }

        foreach (var opportunity in exactContext.Opportunities)
        {
            var opportunityRecord = AddRecord(
                records,
                provenance,
                citationIds,
                "SalesOpportunity",
                opportunity.Id,
                opportunity.ExternalOpportunityId,
                opportunity.Name,
                $"{opportunity.Stage} opportunity at {opportunity.ProbabilityPercent}% probability.",
                opportunity.CloseDateUtc,
                false,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["externalOpportunityId"] = opportunity.ExternalOpportunityId,
                    ["name"] = opportunity.Name,
                    ["stage"] = opportunity.Stage,
                    ["probabilityPercent"] = opportunity.ProbabilityPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["opportunityType"] = opportunity.OpportunityType,
                    ["isOpen"] = opportunity.IsOpen.ToString()
                });
            citationIndex.OpportunityCitations[opportunity.Id] = opportunityRecord.CitationId;
            relationships.Add(BuildRelationship(
                relationshipIds,
                RelationshipType.AccountToOpportunity,
                "deterministic",
                "CustomerAccount",
                subject.Account.Id.ToString("D"),
                "SalesOpportunity",
                opportunity.Id.ToString("D"),
                1.0m,
                input.Objective,
                "Opportunity carries the account foreign key.",
                [accountCitation.CitationId, opportunityRecord.CitationId]));

            if (!opportunity.IsOpen || IsOutcomeStage(opportunity.Stage))
            {
                var outcome = OutcomeFor(opportunity);
                var outcomeRecord = AddRecord(
                    records,
                    provenance,
                    citationIds,
                    "OutcomeSignal",
                    opportunity.Id,
                    $"OUT-{opportunity.ExternalOpportunityId}",
                    outcome,
                    $"Closed opportunity outcome signal is {outcome}.",
                    opportunity.CloseDateUtc,
                    false,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["objective"] = NormalizeObjective(input.Objective),
                        ["outcome"] = outcome,
                        ["sourceEntityType"] = "SalesOpportunity",
                        ["sourceEntityId"] = opportunity.Id.ToString("D"),
                        ["stage"] = opportunity.Stage,
                        ["score"] = (outcome == "won" ? 1m : -1m).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
                relationships.Add(BuildRelationship(
                    relationshipIds,
                    RelationshipType.AccountToOutcome,
                    "deterministic",
                    "CustomerAccount",
                    subject.Account.Id.ToString("D"),
                    "OutcomeSignal",
                    $"OUT-{opportunity.Id:D}",
                    1.0m,
                    input.Objective,
                    $"Closed opportunity records a {outcome} outcome.",
                    [accountCitation.CitationId, outcomeRecord.CitationId]));

                if (opportunity.CustomerContactId.HasValue
                    && citationIndex.ContactCitations.TryGetValue(opportunity.CustomerContactId.Value, out var contactCitation))
                {
                    relationships.Add(BuildRelationship(
                        relationshipIds,
                        RelationshipType.ContactToOutcome,
                        "deterministic",
                        "CustomerContact",
                        opportunity.CustomerContactId.Value.ToString("D"),
                        "OutcomeSignal",
                        $"OUT-{opportunity.Id:D}",
                        1.0m,
                        input.Objective,
                        $"Closed opportunity records a {outcome} outcome for the linked contact.",
                        [contactCitation, outcomeRecord.CitationId]));
                }
            }
        }

        foreach (var activity in exactContext.SalesActivities)
        {
            var activityRecord = AddRecord(
                records,
                provenance,
                citationIds,
                "SalesActivity",
                activity.Id,
                activity.Id.ToString("N"),
                activity.ActivityType,
                governance.SalesActivitySummary(activity),
                activity.OccurredAtUtc,
                governance.HasMaskedSensitiveDetails,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["activityType"] = activity.ActivityType,
                    ["direction"] = activity.Direction,
                    ["outcome"] = activity.Outcome,
                    ["summary"] = governance.SalesActivitySummary(activity)
                });
            citationIndex.ActivityCitations[activity.Id] = activityRecord.CitationId;
            relationships.Add(BuildRelationship(
                relationshipIds,
                RelationshipType.AccountToSalesActivity,
                "deterministic",
                "CustomerAccount",
                subject.Account.Id.ToString("D"),
                "SalesActivity",
                activity.Id.ToString("D"),
                1.0m,
                input.Objective,
                "Sales activity carries the account foreign key.",
                [accountCitation.CitationId, activityRecord.CitationId]));
            if (activity.CustomerContactId.HasValue
                && citationIndex.ContactCitations.TryGetValue(activity.CustomerContactId.Value, out var contactCitation))
            {
                relationships.Add(BuildRelationship(
                    relationshipIds,
                    RelationshipType.ContactToSalesActivity,
                    "deterministic",
                    "CustomerContact",
                    activity.CustomerContactId.Value.ToString("D"),
                    "SalesActivity",
                    activity.Id.ToString("D"),
                    1.0m,
                    input.Objective,
                    "Sales activity carries the contact foreign key.",
                    [contactCitation, activityRecord.CitationId]));
            }
        }

        foreach (var engagement in exactContext.EmailEvents)
        {
            var engagementRecord = AddRecord(
                records,
                provenance,
                citationIds,
                "EmailEngagementEvent",
                engagement.Id,
                engagement.Id.ToString("N"),
                engagement.EventType,
                $"{engagement.EventType} on {engagement.CampaignName}.",
                engagement.OccurredAtUtc,
                false,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["campaignName"] = engagement.CampaignName,
                    ["eventType"] = engagement.EventType,
                    ["channel"] = engagement.Channel
                });
            citationIndex.EmailEventCitations[engagement.Id] = engagementRecord.CitationId;
            relationships.Add(BuildRelationship(
                relationshipIds,
                RelationshipType.ContactToEmailEngagement,
                "deterministic",
                "CustomerContact",
                engagement.CustomerContactId.ToString("D"),
                "EmailEngagementEvent",
                engagement.Id.ToString("D"),
                1.0m,
                input.Objective,
                "Email engagement event carries the contact foreign key.",
                [engagementRecord.CitationId]));
        }

        foreach (var webEvent in exactContext.WebEvents)
        {
            var webRecord = AddRecord(
                records,
                provenance,
                citationIds,
                "WebConversionEvent",
                webEvent.Id,
                webEvent.Id.ToString("N"),
                webEvent.EventType,
                $"{webEvent.EventType} on {webEvent.Page}.",
                webEvent.OccurredAtUtc,
                false,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["eventType"] = webEvent.EventType,
                    ["page"] = webEvent.Page,
                    ["campaign"] = webEvent.Campaign,
                    ["intentScore"] = webEvent.IntentScore.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
            citationIndex.WebEventCitations[webEvent.Id] = webRecord.CitationId;
            relationships.Add(BuildRelationship(
                relationshipIds,
                RelationshipType.AccountToWebConversion,
                "deterministic",
                "CustomerAccount",
                subject.Account.Id.ToString("D"),
                "WebConversionEvent",
                webEvent.Id.ToString("D"),
                1.0m,
                input.Objective,
                "Web conversion event carries the account foreign key.",
                [accountCitation.CitationId, webRecord.CitationId]));
            if (webEvent.CustomerContactId.HasValue)
            {
                relationships.Add(BuildRelationship(
                    relationshipIds,
                    RelationshipType.ContactToWebConversion,
                    "deterministic",
                    "CustomerContact",
                    webEvent.CustomerContactId.Value.ToString("D"),
                    "WebConversionEvent",
                    webEvent.Id.ToString("D"),
                    1.0m,
                    input.Objective,
                    "Web conversion event carries the contact foreign key.",
                    [webRecord.CitationId]));
            }
        }

        foreach (var ticket in exactContext.SupportTickets)
        {
            var supportRecord = AddRecord(
                records,
                provenance,
                citationIds,
                "SupportTicket",
                ticket.Id,
                ticket.ExternalTicketId,
                $"{ticket.Severity} {ticket.Category}",
                governance.SupportTicketSummary(ticket),
                ticket.OpenedAtUtc,
                governance.HasMaskedSensitiveDetails,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["externalTicketId"] = ticket.ExternalTicketId,
                    ["severity"] = ticket.Severity,
                    ["status"] = ticket.Status,
                    ["category"] = ticket.Category,
                    ["subject"] = governance.SupportSubject(ticket)
                });
            citationIndex.SupportTicketCitations[ticket.Id] = supportRecord.CitationId;
            relationships.Add(BuildRelationship(
                relationshipIds,
                RelationshipType.AccountToSupportTicket,
                "deterministic",
                "CustomerAccount",
                subject.Account.Id.ToString("D"),
                "SupportTicket",
                ticket.Id.ToString("D"),
                1.0m,
                input.Objective,
                "Support ticket carries the account foreign key.",
                [accountCitation.CitationId, supportRecord.CitationId]));
            if (ticket.CustomerContactId.HasValue)
            {
                relationships.Add(BuildRelationship(
                    relationshipIds,
                    RelationshipType.ContactToSupportTicket,
                    "deterministic",
                    "CustomerContact",
                    ticket.CustomerContactId.Value.ToString("D"),
                    "SupportTicket",
                    ticket.Id.ToString("D"),
                    1.0m,
                    input.Objective,
                    "Support ticket carries the contact foreign key.",
                    [supportRecord.CitationId]));
            }
        }

        foreach (var usage in exactContext.UsageSummaries)
        {
            var usageRecord = AddRecord(
                records,
                provenance,
                citationIds,
                "ProductUsageSummary",
                usage.Id,
                usage.Id.ToString("N"),
                usage.SummaryDateUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                $"{usage.ActiveDays30} active days, {usage.FeatureAdoptionScore} feature adoption score.",
                usage.SummaryDateUtc,
                false,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["activeDays30"] = usage.ActiveDays30.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["sessions7d"] = usage.Sessions7d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["keyFeatureEvents7d"] = usage.KeyFeatureEvents7d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["pricingPageVisits30d"] = usage.PricingPageVisits30d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["automationRuns30d"] = usage.AutomationRuns30d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["featureAdoptionScore"] = usage.FeatureAdoptionScore.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
            citationIndex.UsageCitations[usage.Id] = usageRecord.CitationId;
            relationships.Add(BuildRelationship(
                relationshipIds,
                RelationshipType.AccountToProductUsage,
                "deterministic",
                "CustomerAccount",
                subject.Account.Id.ToString("D"),
                "ProductUsageSummary",
                usage.Id.ToString("D"),
                1.0m,
                input.Objective,
                "Product usage summary carries the account foreign key.",
                [accountCitation.CitationId, usageRecord.CitationId]));
            if (usage.CustomerContactId.HasValue)
            {
                relationships.Add(BuildRelationship(
                    relationshipIds,
                    RelationshipType.ContactToProductUsage,
                    "deterministic",
                    "CustomerContact",
                    usage.CustomerContactId.Value.ToString("D"),
                    "ProductUsageSummary",
                    usage.Id.ToString("D"),
                    1.0m,
                    input.Objective,
                    "Product usage summary carries the contact foreign key.",
                    [usageRecord.CitationId]));
            }
        }

        foreach (var billing in exactContext.BillingMetrics)
        {
            var billingRecord = AddRecord(
                records,
                provenance,
                citationIds,
                "BillingMetric",
                billing.Id,
                billing.Id.ToString("N"),
                billing.BillingStatus,
                $"{billing.BillingStatus} billing status with {billing.DaysPastDue} days past due.",
                billing.MetricDateUtc,
                governance.HasMaskedBillingAmounts,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["billingStatus"] = billing.BillingStatus,
                    ["monthlyRecurringRevenue"] = governance.Money(billing.MonthlyRecurringRevenue, "billing.monthlyRecurringRevenue"),
                    ["annualRecurringRevenue"] = governance.Money(billing.AnnualRecurringRevenue, "billing.annualRecurringRevenue"),
                    ["daysPastDue"] = billing.DaysPastDue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["paymentFailures30d"] = billing.PaymentFailures30d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["expansionSeatDelta"] = billing.ExpansionSeatDelta.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
            citationIndex.BillingCitations[billing.Id] = billingRecord.CitationId;
            relationships.Add(BuildRelationship(
                relationshipIds,
                RelationshipType.AccountToBilling,
                "deterministic",
                "CustomerAccount",
                subject.Account.Id.ToString("D"),
                "BillingMetric",
                billing.Id.ToString("D"),
                1.0m,
                input.Objective,
                "Billing metric carries the account foreign key.",
                [accountCitation.CitationId, billingRecord.CitationId]));
        }

        return citationIndex;
    }

    private async Task<PatternContext> BuildSimilarPatternsAsync(
        Guid customerOpsTenantId,
        NextActionInput input,
        SubjectContext subject,
        ExactContext exactContext,
        GovernanceContext governance,
        CitationIdAllocator citationIds,
        RelationshipIdAllocator relationshipIds,
        List<RelationshipResult> relationships,
        List<ProvenanceCitationResult> provenance,
        CancellationToken cancellationToken)
    {
        var subjectProfile = BuildProfile(subject.PrimaryContact, subject.Account, exactContext);
        if (subjectProfile is null)
        {
            return new PatternContext([]);
        }

        var candidates = await customerOpsDbContext.CustomerContacts
            .AsNoTracking()
            .Include(x => x.Account)
            .Where(x => x.CustomerOpsTenantId == customerOpsTenantId && x.Id != subjectProfile.Contact.Id)
            .Take(250)
            .ToListAsync(cancellationToken);
        var candidateAccountIds = candidates.Select(x => x.CustomerAccountId).Distinct().ToArray();
        var candidateContactIds = candidates.Select(x => x.Id).ToArray();

        var opportunities = await customerOpsDbContext.SalesOpportunities
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == customerOpsTenantId && candidateAccountIds.Contains(x.CustomerAccountId))
            .ToListAsync(cancellationToken);
        var usages = await customerOpsDbContext.ProductUsageSummaries
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == customerOpsTenantId && x.CustomerContactId.HasValue && candidateContactIds.Contains(x.CustomerContactId.Value))
            .ToListAsync(cancellationToken);
        var emailEvents = await customerOpsDbContext.EmailEngagementEvents
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == customerOpsTenantId && candidateContactIds.Contains(x.CustomerContactId))
            .ToListAsync(cancellationToken);
        var webEvents = await customerOpsDbContext.WebConversionEvents
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == customerOpsTenantId && x.CustomerContactId.HasValue && candidateContactIds.Contains(x.CustomerContactId.Value))
            .ToListAsync(cancellationToken);
        var supportTickets = await customerOpsDbContext.SupportTickets
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == customerOpsTenantId && candidateAccountIds.Contains(x.CustomerAccountId))
            .ToListAsync(cancellationToken);
        var billingMetrics = await customerOpsDbContext.BillingMetrics
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == customerOpsTenantId && candidateAccountIds.Contains(x.CustomerAccountId))
            .ToListAsync(cancellationToken);

        var patterns = new List<SimilarPatternMatchResult>();
        foreach (var candidate in candidates)
        {
            var candidateProfile = BuildProfile(
                candidate,
                candidate.Account,
                new ExactContext(
                    opportunities.Where(x => x.CustomerAccountId == candidate.CustomerAccountId).ToList(),
                    [],
                    emailEvents.Where(x => x.CustomerContactId == candidate.Id).ToList(),
                    webEvents.Where(x => x.CustomerContactId == candidate.Id).ToList(),
                    supportTickets.Where(x => x.CustomerAccountId == candidate.CustomerAccountId).ToList(),
                    usages.Where(x => x.CustomerContactId == candidate.Id).ToList(),
                    billingMetrics.Where(x => x.CustomerAccountId == candidate.CustomerAccountId).ToList(),
                    clock.UtcNow.AddDays(-30)));
            if (candidateProfile is null || candidateProfile.Outcome == "open")
            {
                continue;
            }

            var similarity = ComputeSimilarity(subjectProfile, candidateProfile);
            if (similarity.Score < 0.45m)
            {
                continue;
            }

            var citationId = citationIds.NextPattern();
            var outcomeWeight = candidateProfile.Outcome == "won"
                ? similarity.Score
                : -similarity.Score;
            var match = new SimilarPatternMatchResult(
                $"MATCH-{candidate.Id:N}",
                "CustomerContact",
                governance.PatternSubjectId(candidate),
                governance.PatternAccountId(candidate.Account),
                candidateProfile.Outcome,
                Math.Round(similarity.Score, 4),
                Math.Round(outcomeWeight, 4),
                similarity.RelationshipTypes.Select(x => x.ToString()).ToList(),
                similarity.Reasons,
                [citationId]);
            patterns.Add(match);
            provenance.Add(new ProvenanceCitationResult(
                citationId,
                "SimilarPatternMatch",
                match.MatchId,
                "similarity",
                $"Similar {candidateProfile.Outcome} pattern matched on {string.Join(", ", similarity.RelationshipTypes)}.",
                IsMasked: governance.HasMaskedPii || governance.HasMaskedBusinessDetails));

            foreach (var relationshipType in similarity.RelationshipTypes)
            {
                relationships.Add(BuildRelationship(
                    relationshipIds,
                    relationshipType,
                    "probabilistic",
                    "CustomerContact",
                    subjectProfile.Contact.Id.ToString("D"),
                    "CustomerContact",
                    candidate.Id.ToString("D"),
                    similarity.Score,
                    input.Objective,
                    $"Similarity matched: {string.Join("; ", similarity.Reasons)}",
                    [citationId]));
            }
        }

        return new PatternContext(
            patterns
                .OrderByDescending(x => Math.Abs(x.OutcomeWeight))
                .ThenByDescending(x => x.SimilarityScore)
                .Take(5)
                .ToList());
    }

    private static List<WeightedSignalResult> BuildWeightedSignals(
        NextActionInput input,
        SubjectContext subject,
        ExactContext exactContext,
        IReadOnlyList<SimilarPatternMatchResult> patterns,
        CitationIndex citationIndex)
    {
        var latestUsage = exactContext.LatestUsage;
        var latestBilling = exactContext.LatestBilling;
        var openSupport = exactContext.OpenSupportTickets.Count;
        var severeSupport = exactContext.OpenSupportTickets.Count(x => x.Severity == "critical" || x.Severity == "high");
        var pricingVisits = Math.Max(
            latestUsage?.PricingPageVisits30d ?? 0,
            exactContext.WebEvents.Count(x => x.Page == "pricing" && x.OccurredAtUtc >= exactContext.Cutoff30d));
        var emailReplies = exactContext.EmailEvents.Count(x =>
            (x.EventType == "reply" || x.EventType == "meeting_booked") && x.OccurredAtUtc >= exactContext.Cutoff30d);
        var activeUsageScore = latestUsage is null
            ? 0m
            : Clamp01((latestUsage.ActiveDays30 / 30m * 0.45m)
                + (latestUsage.FeatureAdoptionScore / 100m * 0.35m)
                + (latestUsage.AutomationRuns30d / 120m * 0.20m));
        var openOpportunityProbability = exactContext.OpenOpportunities.Select(x => x.ProbabilityPercent).DefaultIfEmpty(0).Max();
        var supportScore = Clamp01(openSupport * 0.28m + severeSupport * 0.35m);
        var billingScore = latestBilling is null
            ? 0m
            : Clamp01((latestBilling.DaysPastDue / 30m) + (latestBilling.PaymentFailures30d * 0.35m));
        var similarWonScore = Clamp01(patterns.Count(x => x.Outcome == "won") * 0.34m);
        var similarLostScore = Clamp01(patterns.Count(x => x.Outcome == "lost") * 0.34m);
        var fallback = citationIndex.FallbackCitations();

        return
        [
            BuildSignal(
                "pricing-intent",
                "Pricing journey intent",
                "positive",
                0.18m,
                Clamp01(pricingVisits / 8m),
                $"Pricing was visited {pricingVisits} time(s) in the current journey.",
                citationIndex.WebEventCitations.Values.Take(4).DefaultIfEmpty(citationIndex.Account).WhereNotNull().ToList()),
            BuildSignal(
                "email-response",
                "Email response pattern",
                "positive",
                0.16m,
                Clamp01(emailReplies / 3m),
                $"{emailReplies} recent email reply or meeting-booked event(s) were linked to the contact.",
                citationIndex.EmailEventCitations.Values.Take(4).DefaultIfEmpty(citationIndex.Account).WhereNotNull().ToList()),
            BuildSignal(
                "active-usage",
                "Active product usage",
                "positive",
                0.16m,
                activeUsageScore,
                latestUsage is null
                    ? "No recent product usage summary is linked."
                    : $"{latestUsage.ActiveDays30} active days and {latestUsage.FeatureAdoptionScore} feature adoption score are linked.",
                citationIndex.UsageCitations.Values.Take(3).DefaultIfEmpty(citationIndex.Account).WhereNotNull().ToList()),
            BuildSignal(
                "opportunity-momentum",
                "Opportunity momentum",
                "positive",
                0.14m,
                Clamp01(openOpportunityProbability / 100m),
                $"The strongest open opportunity is at {openOpportunityProbability}% probability.",
                citationIndex.OpportunityCitations.Values.Take(3).DefaultIfEmpty(citationIndex.Account).WhereNotNull().ToList()),
            BuildSignal(
                "decision-maker",
                "Decision-maker fit",
                "positive",
                0.08m,
                subject.PrimaryContact?.IsDecisionMaker == true ? 1m : 0.35m,
                subject.PrimaryContact?.IsDecisionMaker == true
                    ? "The primary contact is marked as a decision maker."
                    : "The primary contact is not marked as a decision maker.",
                fallback),
            BuildSignal(
                "similar-won-patterns",
                "Similar won patterns",
                "positive",
                0.13m,
                similarWonScore,
                $"{patterns.Count(x => x.Outcome == "won")} similar won pattern(s) were found.",
                patterns.Where(x => x.Outcome == "won").SelectMany(x => x.CitationIds).Take(3).DefaultIfEmpty(citationIndex.Account).WhereNotNull().ToList()),
            BuildSignal(
                "support-blockers",
                "Support blockers",
                "negative",
                0.18m,
                supportScore,
                $"{openSupport} open support ticket(s), including {severeSupport} severe blocker(s), are linked.",
                citationIndex.SupportTicketCitations.Values.Take(4).DefaultIfEmpty(citationIndex.Account).WhereNotNull().ToList()),
            BuildSignal(
                "billing-blockers",
                "Billing blockers",
                "negative",
                0.14m,
                billingScore,
                latestBilling is null
                    ? "No current billing blocker was linked."
                    : $"{latestBilling.DaysPastDue} days past due and {latestBilling.PaymentFailures30d} payment failure(s) are linked.",
                citationIndex.BillingCitations.Values.Take(3).DefaultIfEmpty(citationIndex.Account).WhereNotNull().ToList()),
            BuildSignal(
                "similar-lost-patterns",
                "Similar lost patterns",
                "negative",
                0.08m,
                similarLostScore,
                $"{patterns.Count(x => x.Outcome == "lost")} similar lost pattern(s) were found.",
                patterns.Where(x => x.Outcome == "lost").SelectMany(x => x.CitationIds).Take(3).DefaultIfEmpty(citationIndex.Account).WhereNotNull().ToList())
        ];
    }

    private static WeightedSignalResult BuildSignal(
        string signalKey,
        string label,
        string direction,
        decimal weight,
        decimal score,
        string explanation,
        IReadOnlyList<string> citationIds)
    {
        var resolvedScore = Clamp01(score);
        var contribution = Math.Round(resolvedScore * weight * (direction == "negative" ? -1m : 1m), 4);
        return new WeightedSignalResult(
            signalKey,
            label,
            direction,
            weight,
            Math.Round(resolvedScore, 4),
            contribution,
            explanation,
            citationIds.Count == 0 ? [] : citationIds);
    }

    private static decimal BuildConfidence(
        decimal score,
        ExactContext exactContext,
        IReadOnlyList<SimilarPatternMatchResult> patterns)
    {
        var evidenceCompleteness = 0.42m;
        if (exactContext.LatestUsage is not null)
        {
            evidenceCompleteness += 0.12m;
        }

        if (exactContext.EmailEvents.Count > 0)
        {
            evidenceCompleteness += 0.09m;
        }

        if (exactContext.WebEvents.Count > 0)
        {
            evidenceCompleteness += 0.09m;
        }

        if (exactContext.Opportunities.Count > 0)
        {
            evidenceCompleteness += 0.08m;
        }

        if (patterns.Count > 0)
        {
            evidenceCompleteness += 0.08m;
        }

        var blockerPenalty = Math.Min(0.28m, exactContext.OpenSupportTickets.Count * 0.07m)
            + Math.Min(0.22m, (exactContext.LatestBilling?.DaysPastDue ?? 0) * 0.01m)
            + Math.Min(0.16m, (exactContext.LatestBilling?.PaymentFailures30d ?? 0) * 0.08m);
        return Math.Round(Clamp01((score * 0.55m) + evidenceCompleteness - blockerPenalty), 4);
    }

    private static List<string> BuildCaveats(ExactContext exactContext, IReadOnlyList<SimilarPatternMatchResult> patterns)
    {
        var caveats = new List<string>();
        var freshestOperationalSignalUtc = FreshestOperationalSignalUtc(exactContext);
        var exactEvidenceCategories = ExactEvidenceCategoryCount(exactContext);
        var latestBilling = exactContext.LatestBilling;
        var hasCommercialMomentum = exactContext.OpenOpportunities.Any(x => x.ProbabilityPercent >= 70)
            || exactContext.EmailEvents.Any(x => (x.EventType == "reply" || x.EventType == "meeting_booked") && x.OccurredAtUtc >= exactContext.Cutoff30d)
            || exactContext.WebEvents.Count(x => x.Page == "pricing" && x.OccurredAtUtc >= exactContext.Cutoff30d) >= 3
            || (exactContext.LatestUsage is not null
                && exactContext.LatestUsage.SummaryDateUtc >= exactContext.Cutoff30d
                && exactContext.LatestUsage.ActiveDays30 >= 18
                && exactContext.LatestUsage.FeatureAdoptionScore >= 65);
        var hasOperationalBlockers = exactContext.OpenSupportTickets.Count > 0
            || (latestBilling?.DaysPastDue ?? 0) > 0
            || (latestBilling?.PaymentFailures30d ?? 0) > 0;

        if (freshestOperationalSignalUtc.HasValue && freshestOperationalSignalUtc.Value < exactContext.Cutoff30d)
        {
            caveats.Add("The freshest linked operational signal is outside the current 30-day decision window.");
        }

        if (hasCommercialMomentum && hasOperationalBlockers)
        {
            caveats.Add("Conflicting exact data is present: intent or opportunity momentum is linked alongside support or billing blockers.");
        }

        if (exactEvidenceCategories <= 1)
        {
            caveats.Add("Insufficient exact operational evidence is linked for a high-confidence recommendation.");
        }

        if (exactContext.EmailEvents.Count == 0)
        {
            caveats.Add("No linked email engagement events were found for the subject.");
        }

        if (exactContext.WebEvents.Count == 0)
        {
            caveats.Add("No linked web conversion events were found for the subject.");
        }

        if (patterns.Count == 0)
        {
            caveats.Add("No sufficiently similar won/lost pattern was found in the tenant data plane.");
        }

        if (exactContext.OpenSupportTickets.Count > 0)
        {
            caveats.Add("Open support tickets reduce confidence and should be reviewed before acting.");
        }

        var billing = exactContext.LatestBilling;
        if (billing is not null && (billing.DaysPastDue > 0 || billing.PaymentFailures30d > 0))
        {
            caveats.Add("Billing blockers reduce confidence and should be cleared or acknowledged.");
        }

        return caveats;
    }

    private static DateTime? FreshestOperationalSignalUtc(ExactContext exactContext)
    {
        var observedDates = exactContext.Opportunities.Select(x => x.CloseDateUtc)
            .Concat(exactContext.SalesActivities.Select(x => x.OccurredAtUtc))
            .Concat(exactContext.EmailEvents.Select(x => x.OccurredAtUtc))
            .Concat(exactContext.WebEvents.Select(x => x.OccurredAtUtc))
            .Concat(exactContext.SupportTickets.Select(x => x.OpenedAtUtc))
            .Concat(exactContext.UsageSummaries.Select(x => x.SummaryDateUtc))
            .Concat(exactContext.BillingMetrics.Select(x => x.MetricDateUtc))
            .ToList();

        return observedDates.Count == 0 ? null : observedDates.Max();
    }

    private static int ExactEvidenceCategoryCount(ExactContext exactContext)
    {
        var count = 0;
        if (exactContext.Opportunities.Count > 0)
        {
            count++;
        }

        if (exactContext.SalesActivities.Count > 0)
        {
            count++;
        }

        if (exactContext.EmailEvents.Count > 0)
        {
            count++;
        }

        if (exactContext.WebEvents.Count > 0)
        {
            count++;
        }

        if (exactContext.SupportTickets.Count > 0)
        {
            count++;
        }

        if (exactContext.UsageSummaries.Count > 0)
        {
            count++;
        }

        if (exactContext.BillingMetrics.Count > 0)
        {
            count++;
        }

        return count;
    }

    private static RecommendedNextActionResult BuildRecommendedAction(
        NextActionInput input,
        ExactContext exactContext,
        IReadOnlyList<WeightedSignalResult> signals,
        decimal score)
    {
        var objective = NormalizeObjective(input.Objective);
        var positiveCitations = signals
            .Where(x => x.Direction == "positive" && x.Score > 0m)
            .OrderByDescending(x => x.Contribution)
            .SelectMany(x => x.CitationIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        var blockerCitations = signals
            .Where(x => x.Direction == "negative" && x.Score > 0m)
            .OrderBy(x => x.Contribution)
            .SelectMany(x => x.CitationIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        var citations = blockerCitations.Count > 0
            ? blockerCitations.Concat(positiveCitations).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList()
            : positiveCitations;

        if (objective is "support")
        {
            return new RecommendedNextActionResult(
                "Prioritise the linked support blocker and send a status update",
                "Today",
                "The support objective should be grounded in the open ticket and account activity evidence.",
                Math.Round(score, 4),
                citations);
        }

        if (exactContext.OpenSupportTickets.Count > 0
            || (exactContext.LatestBilling?.DaysPastDue ?? 0) > 0
            || (exactContext.LatestBilling?.PaymentFailures30d ?? 0) > 0)
        {
            return new RecommendedNextActionResult(
                "Resolve commercial blockers before asking for expansion",
                "Before the next commercial call",
                "Support or billing blockers are linked to the account, so confidence is reduced even when intent signals are present.",
                Math.Round(score, 4),
                citations);
        }

        if (objective is "sale" or "conversion")
        {
            return score >= 0.72m
                ? new RecommendedNextActionResult(
                    "Send a focused executive follow-up and propose a pricing or implementation call",
                    "Within one business day",
                    "Pricing visits, email engagement, active usage, and similar won patterns indicate a timely commercial motion.",
                    Math.Round(score, 4),
                    citations)
                : new RecommendedNextActionResult(
                    "Nurture with a value proof and ask one qualifying question",
                    "This week",
                    "The exact links show some commercial intent, but the evidence is not strong enough for a direct close ask.",
                    Math.Round(score, 4),
                    citations);
        }

        return new RecommendedNextActionResult(
            "Prepare a targeted retention check-in grounded in usage and blocker evidence",
            "This week",
            "Retention confidence depends on product usage, support drag, billing health, and similar outcomes.",
            Math.Round(score, 4),
            citations);
    }

    private static DraftResponseResult? BuildDraftResponse(
        NextActionInput input,
        SubjectContext subject,
        GovernanceContext governance,
        RecommendedNextActionResult action)
    {
        var objective = NormalizeObjective(input.Objective);
        if (objective is not ("sale" or "conversion" or "retention" or "support"))
        {
            return null;
        }

        var contact = subject.PrimaryContact;
        var greeting = contact is null || governance.HasMaskedPii
            ? "Hi there"
            : $"Hi {contact.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there"}";
        var accountName = governance.AccountName(subject.Account);
        var subjectLine = objective == "support"
            ? $"Next step on your {accountName} support path"
            : $"Next step for {accountName}";
        var body = objective == "support"
            ? $"{greeting},\n\nI reviewed the linked account evidence and the active blocker. I suggest we prioritise the support issue first, then confirm the next commercial or retention step once the blocker is clear.\n\nBest,"
            : $"{greeting},\n\nI reviewed the linked account evidence and there is enough intent to suggest a focused follow-up. The cleanest next step is to connect on the pricing or implementation path and confirm the decision criteria.\n\nBest,";

        return new DraftResponseResult(
            contact?.PreferredChannel ?? "email",
            subjectLine,
            body,
            action.CitationIds,
            RequiresHumanReview: governance.HasMaskedPii || action.Score < 0.65m);
    }

    private static PatternProfile? BuildProfile(CustomerContact? contact, CustomerAccount account, ExactContext exactContext)
    {
        if (contact is null)
        {
            return null;
        }

        var latestUsage = exactContext.LatestUsage;
        var latestBilling = exactContext.LatestBilling;
        var outcomeOpportunity = exactContext.Opportunities
            .Where(x => !x.IsOpen || IsOutcomeStage(x.Stage))
            .OrderByDescending(x => x.CloseDateUtc)
            .FirstOrDefault();
        var openStage = exactContext.OpenOpportunities
            .OrderByDescending(x => x.ProbabilityPercent)
            .Select(x => x.Stage)
            .FirstOrDefault() ?? string.Empty;

        return new PatternProfile(
            contact,
            account,
            latestUsage?.ActiveDays30 ?? 0,
            latestUsage?.FeatureAdoptionScore ?? 0,
            latestUsage?.PricingPageVisits30d ?? exactContext.WebEvents.Count(x => x.Page == "pricing"),
            latestUsage?.AutomationRuns30d ?? 0,
            exactContext.EmailEvents.Count(x => x.EventType == "reply" || x.EventType == "meeting_booked"),
            exactContext.WebEvents.Any(x => x.EventType == "trial_activated"),
            exactContext.OpenSupportTickets.Count,
            exactContext.OpenSupportTickets.Count(x => x.Severity == "critical" || x.Severity == "high"),
            latestBilling?.DaysPastDue ?? 0,
            latestBilling?.PaymentFailures30d ?? 0,
            outcomeOpportunity is null ? "open" : OutcomeFor(outcomeOpportunity),
            outcomeOpportunity?.Stage ?? openStage);
    }

    private static SimilarityScore ComputeSimilarity(PatternProfile subject, PatternProfile candidate)
    {
        var score = 0m;
        var reasons = new List<string>();
        var types = new List<RelationshipType>();

        if (string.Equals(subject.Account.Domain, candidate.Account.Domain, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.10m;
            reasons.Add("same account domain");
            types.Add(RelationshipType.SameDomain);
        }

        if (string.Equals(subject.Account.Industry, candidate.Account.Industry, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.18m;
            reasons.Add($"same target domain: {subject.Account.Industry}");
            types.Add(RelationshipType.SameDomain);
        }

        if (string.Equals(subject.Account.Segment, candidate.Account.Segment, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.16m;
            reasons.Add($"same segment: {subject.Account.Segment}");
            types.Add(RelationshipType.SameSegment);
        }

        if (string.Equals(subject.Contact.Seniority, candidate.Contact.Seniority, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.14m;
            reasons.Add($"same seniority: {subject.Contact.Seniority}");
            types.Add(RelationshipType.SameRoleSeniority);
        }

        var usageSimilarity = SimilarityFromDistance(
            subject.ActiveDays30,
            candidate.ActiveDays30,
            30,
            subject.FeatureAdoptionScore,
            candidate.FeatureAdoptionScore,
            100,
            subject.AutomationRuns30,
            candidate.AutomationRuns30,
            120);
        if (usageSimilarity >= 0.58m)
        {
            score += usageSimilarity * 0.16m;
            reasons.Add("similar product usage pattern");
            types.Add(RelationshipType.SimilarProductUsagePattern);
        }

        var webSimilarity = SimilarityFromDistance(
            subject.PricingVisits30,
            candidate.PricingVisits30,
            12,
            subject.TrialActivated ? 1 : 0,
            candidate.TrialActivated ? 1 : 0,
            1);
        if (webSimilarity >= 0.58m)
        {
            score += webSimilarity * 0.10m;
            reasons.Add("similar web journey");
            types.Add(RelationshipType.SimilarWebJourney);
        }

        var emailSimilarity = SimilarityFromDistance(subject.EmailReplies30, candidate.EmailReplies30, 5);
        if (emailSimilarity >= 0.58m)
        {
            score += emailSimilarity * 0.10m;
            reasons.Add("similar email response pattern");
            types.Add(RelationshipType.SimilarEmailResponsePattern);
        }

        var supportSimilarity = SimilarityFromDistance(
            subject.OpenSupportTickets,
            candidate.OpenSupportTickets,
            5,
            subject.SevereSupportTickets,
            candidate.SevereSupportTickets,
            3);
        if (supportSimilarity >= 0.58m)
        {
            score += supportSimilarity * 0.08m;
            reasons.Add("similar support blocker profile");
            types.Add(RelationshipType.SimilarSupportBlockers);
        }

        if (candidate.Outcome == "won"
            && (string.Equals(subject.OpportunityStage, candidate.OpportunityStage, StringComparison.OrdinalIgnoreCase)
                || subject.OpportunityStage is "proposal" or "negotiation"))
        {
            score += 0.16m;
            reasons.Add("similar successful sale path");
            types.Add(RelationshipType.SimilarSuccessfulSalePath);
        }

        return new SimilarityScore(
            Clamp01(score),
            types.Distinct().ToList(),
            reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static ExactLinkedRecordSummaryResult AddRecord(
        List<ExactLinkedRecordSummaryResult> records,
        List<ProvenanceCitationResult> provenance,
        CitationIdAllocator citationIds,
        string recordType,
        Guid recordId,
        string externalId,
        string label,
        string summary,
        DateTime? observedAtUtc,
        bool isMasked,
        IReadOnlyDictionary<string, string> fields)
    {
        var citationId = citationIds.NextEvidence();
        var result = new ExactLinkedRecordSummaryResult(
            citationId,
            recordType,
            recordId.ToString("D"),
            externalId,
            label,
            summary,
            observedAtUtc,
            isMasked,
            fields);
        records.Add(result);
        provenance.Add(new ProvenanceCitationResult(
            citationId,
            recordType,
            recordId.ToString("D"),
            "exact-record",
            summary,
            isMasked));
        return result;
    }

    private RelationshipResult BuildRelationship(
        RelationshipIdAllocator relationshipIds,
        RelationshipType relationshipType,
        string linkKind,
        string sourceType,
        string sourceId,
        string targetType,
        string targetId,
        decimal confidence,
        string objective,
        string rationale,
        IReadOnlyList<string> citationIds)
        => relationshipEngine.BuildRelationship(
            relationshipIds.Next(),
            relationshipType,
            linkKind,
            sourceType,
            sourceId,
            targetType,
            targetId,
            confidence,
            objective,
            rationale,
            citationIds);

    private static UclCloudAggregateUsageV1 BuildCloudAggregateUsagePayload(
        string tenantSlug,
        DateTime generatedAtUtc,
        GovernanceContext governance)
        => new(
            UclEvidencePackContractVersions.CloudAggregateUsageKind,
            UclEvidencePackContractVersions.CloudAggregateUsageV1,
            PackageVersion,
            tenantSlug,
            "next-action",
            "intelligence.next-action.generated",
            "succeeded",
            generatedAtUtc,
            new UclCloudFeatureUsageCountersV1(
                NextActionGenerateRequests: 1,
                DataPlanePackageBuilds: 1),
            new UclCloudControlPlaneCountersV1(
                governance.AppliedRules.Count,
                governance.MaskedFields.Count,
                governance.DeniedFields.Count),
            new UclCloudDataBoundaryV1(
                RawDataRetainedInCustomerDataPlane: true,
                ContainsRawCustomerData: false,
                ContainsRecords: false,
                ContainsFacts: false,
                ContainsContextFacts: false,
                ContainsSnapshots: false,
                ContainsContextSnapshots: false,
                ContainsEvidencePacks: false,
                ContainsPrompts: false,
                ContainsGeneratedContent: false,
                ContainsRecommendations: false,
                ContainsCitations: false,
                ContainsCitationIds: false,
                ContainsRelationshipTypes: false,
                ContainsWeightedSignals: false,
                ContainsCaveats: false,
                ContainsPerEntityRelationshipMetadata: false,
                ContainsDerivedRelationshipIntelligence: false,
                ContainsPerCustomerDerivedIntelligence: false));

    private static decimal SimilarityFromDistance(params int[] values)
    {
        if (values.Length % 3 != 0)
        {
            throw new InvalidOperationException("Similarity values must be supplied in left/right/max triples.");
        }

        var scores = new List<decimal>();
        for (var index = 0; index < values.Length; index += 3)
        {
            var left = values[index];
            var right = values[index + 1];
            var max = Math.Max(1, values[index + 2]);
            scores.Add(Clamp01(1m - (Math.Abs(left - right) / (decimal)max)));
        }

        return scores.Count == 0 ? 0m : scores.Average();
    }

    private static string OutcomeFor(SalesOpportunity opportunity)
    {
        if (opportunity.Stage.Contains("won", StringComparison.OrdinalIgnoreCase))
        {
            return "won";
        }

        if (opportunity.Stage.Contains("lost", StringComparison.OrdinalIgnoreCase))
        {
            return "lost";
        }

        return opportunity.ProbabilityPercent >= 70 ? "won" : "lost";
    }

    private static bool IsOutcomeStage(string stage)
        => stage.Contains("won", StringComparison.OrdinalIgnoreCase)
            || stage.Contains("lost", StringComparison.OrdinalIgnoreCase);

    private static bool IsOpenSupportStatus(string status)
        => status is not ("closed" or "resolved");

    private static string NormalizeSlug(string value) => value.Trim().ToLowerInvariant();

    private static string NormalizeSubjectType(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "email" => "email",
            "contact" => "contact",
            "account" => "account",
            _ => throw new InvalidOperationException("SubjectType must be email, contact, or account.")
        };

    private static string NormalizeObjective(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "sales" => "sale",
            "sell" => "sale",
            "conversion" => "conversion",
            "convert" => "conversion",
            "churn" => "churn",
            "support" => "support",
            "retain" => "retention",
            "retention" => "retention",
            "sale" => "sale",
            _ => value.Trim().ToLowerInvariant()
        };

    private static decimal Clamp01(decimal value) => Math.Clamp(value, 0m, 1m);

    private static string MaskEmail(string email)
    {
        var separator = email.IndexOf('@', StringComparison.Ordinal);
        if (separator <= 1)
        {
            return "***";
        }

        return $"{email[0]}***{email[separator..]}";
    }

    private static string MaskName(string name)
    {
        var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? "***" : $"{first} ***";
    }

    private static string HashValue(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static OperatorRole ResolveEffectiveRole(ActorContext actor, string requestedActorRole)
    {
        var requested = ParseRole(requestedActorRole);
        if (actor.IsSystem)
        {
            return requested;
        }

        if (actor.Role == OperatorRole.ApiClient)
        {
            return requested is OperatorRole.PlatformOwner or OperatorRole.TenantAdmin
                ? OperatorRole.ReadOnly
                : requested;
        }

        return actor.Role;
    }

    private static OperatorRole ParseRole(string role)
        => role.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal) switch
        {
            "platform_owner" => OperatorRole.PlatformOwner,
            "tenant_admin" => OperatorRole.TenantAdmin,
            "integration_admin" => OperatorRole.IntegrationAdmin,
            "analyst" => OperatorRole.Analyst,
            "sales_rep" or "sales_user" => OperatorRole.SalesUser,
            "api_client" => OperatorRole.ApiClient,
            "read_only" or "readonly" => OperatorRole.ReadOnly,
            _ => OperatorRole.ReadOnly
        };

    private static string RoleToContractValue(OperatorRole role)
        => role switch
        {
            OperatorRole.PlatformOwner => "platform_owner",
            OperatorRole.TenantAdmin => "tenant_admin",
            OperatorRole.IntegrationAdmin => "integration_admin",
            OperatorRole.Analyst => "analyst",
            OperatorRole.SalesUser => "sales_rep",
            OperatorRole.ApiClient => "api_client",
            _ => "read_only"
        };

    private sealed record SubjectContext(
        string SubjectType,
        string SubjectIdentifier,
        CustomerAccount Account,
        IReadOnlyList<CustomerContact> Contacts,
        CustomerContact? PrimaryContact);

    private sealed record ExactContext(
        IReadOnlyList<SalesOpportunity> Opportunities,
        IReadOnlyList<SalesActivity> SalesActivities,
        IReadOnlyList<EmailEngagementEvent> EmailEvents,
        IReadOnlyList<WebConversionEvent> WebEvents,
        IReadOnlyList<SupportTicket> SupportTickets,
        IReadOnlyList<ProductUsageSummary> UsageSummaries,
        IReadOnlyList<BillingMetric> BillingMetrics,
        DateTime Cutoff30d)
    {
        public ProductUsageSummary? LatestUsage => UsageSummaries.OrderByDescending(x => x.SummaryDateUtc).FirstOrDefault();

        public BillingMetric? LatestBilling => BillingMetrics.OrderByDescending(x => x.MetricDateUtc).FirstOrDefault();

        public IReadOnlyList<SalesOpportunity> OpenOpportunities => Opportunities.Where(x => x.IsOpen).ToList();

        public IReadOnlyList<SupportTicket> OpenSupportTickets => SupportTickets.Where(x => IsOpenSupportStatus(x.Status)).ToList();
    }

    private sealed record PatternProfile(
        CustomerContact Contact,
        CustomerAccount Account,
        int ActiveDays30,
        int FeatureAdoptionScore,
        int PricingVisits30,
        int AutomationRuns30,
        int EmailReplies30,
        bool TrialActivated,
        int OpenSupportTickets,
        int SevereSupportTickets,
        int DaysPastDue,
        int PaymentFailures30,
        string Outcome,
        string OpportunityStage);

    private sealed record SimilarityScore(
        decimal Score,
        IReadOnlyList<RelationshipType> RelationshipTypes,
        IReadOnlyList<string> Reasons);

    private sealed record PatternContext(IReadOnlyList<SimilarPatternMatchResult> Patterns);

    private sealed class CitationIndex
    {
        public string? Account { get; set; }

        public Dictionary<Guid, string> ContactCitations { get; } = new();

        public Dictionary<Guid, string> OpportunityCitations { get; } = new();

        public Dictionary<Guid, string> ActivityCitations { get; } = new();

        public Dictionary<Guid, string> EmailEventCitations { get; } = new();

        public Dictionary<Guid, string> WebEventCitations { get; } = new();

        public Dictionary<Guid, string> SupportTicketCitations { get; } = new();

        public Dictionary<Guid, string> UsageCitations { get; } = new();

        public Dictionary<Guid, string> BillingCitations { get; } = new();

        public IReadOnlyList<string> FallbackCitations()
        {
            var citations = new List<string>();
            if (!string.IsNullOrWhiteSpace(Account))
            {
                citations.Add(Account);
            }

            citations.AddRange(ContactCitations.Values.Take(2));
            return citations;
        }
    }

    private sealed class CitationIdAllocator
    {
        private int evidenceIndex;
        private int patternIndex;

        public string NextEvidence() => $"EVID-{++evidenceIndex:00}";

        public string NextPattern() => $"PAT-{++patternIndex:00}";
    }

    private sealed class RelationshipIdAllocator
    {
        private int index;

        public string Next() => $"REL-{++index:00}";
    }

    private sealed class GovernanceContext
    {
        private readonly bool canViewPii;
        private readonly bool canViewBusinessDetails;
        private readonly bool canViewSensitiveDetails;
        private readonly bool canViewBillingAmounts;

        private GovernanceContext(
            bool canViewPii,
            bool canViewBusinessDetails,
            bool canViewSensitiveDetails,
            bool canViewBillingAmounts,
            IReadOnlyList<string> appliedRules)
        {
            this.canViewPii = canViewPii;
            this.canViewBusinessDetails = canViewBusinessDetails;
            this.canViewSensitiveDetails = canViewSensitiveDetails;
            this.canViewBillingAmounts = canViewBillingAmounts;
            AppliedRules = appliedRules;
        }

        public bool HasMaskedPii => !canViewPii;

        public bool HasMaskedBusinessDetails => !canViewBusinessDetails;

        public bool HasMaskedSensitiveDetails => !canViewSensitiveDetails;

        public bool HasMaskedBillingAmounts => !canViewBillingAmounts;

        public IReadOnlyList<string> AppliedRules { get; }

        public HashSet<string> MaskedFields { get; } = new(StringComparer.Ordinal);

        public HashSet<string> DeniedFields { get; } = new(StringComparer.Ordinal);

        public static GovernanceContext Create(ActorContext actor, OperatorRole role, string purpose)
        {
            var canViewPii = actor.IsSystem || actor.CanViewSensitivePii;
            var canViewBusinessDetails = role != OperatorRole.ReadOnly;
            var canViewSensitiveDetails = role is OperatorRole.PlatformOwner or OperatorRole.TenantAdmin or OperatorRole.Analyst;
            var canViewBillingAmounts = role is OperatorRole.PlatformOwner or OperatorRole.TenantAdmin or OperatorRole.Analyst or OperatorRole.SalesUser;
            var rules = new List<string>
            {
                "exact-data-query-runs-in-customer-owned-data-plane",
                "cloud-aggregate-usage-payload-excludes-raw-and-derived-customer-intelligence",
                $"purpose:{purpose.Trim().ToLowerInvariant()}",
                $"actor-role:{RoleToContractValue(role)}"
            };
            if (!canViewPii)
            {
                rules.Add("mask-direct-identifiers");
            }

            if (!canViewSensitiveDetails)
            {
                rules.Add("mask-support-and-activity-detail");
            }

            if (!canViewBusinessDetails)
            {
                rules.Add("mask-business-identifiers");
            }

            return new GovernanceContext(canViewPii, canViewBusinessDetails, canViewSensitiveDetails, canViewBillingAmounts, rules);
        }

        public string Email(string email, string field)
        {
            if (canViewPii)
            {
                return email;
            }

            MaskedFields.Add(field);
            return MaskEmail(email);
        }

        public string ContactName(CustomerContact contact)
        {
            if (canViewPii)
            {
                return contact.FullName;
            }

            MaskedFields.Add("contact.fullName");
            return MaskName(contact.FullName);
        }

        public string ContactLabel(CustomerContact contact)
            => canViewPii ? contact.FullName : $"Contact {contact.ExternalContactId}";

        public string AccountName(CustomerAccount account)
        {
            if (canViewBusinessDetails)
            {
                return account.Name;
            }

            MaskedFields.Add("account.name");
            return $"Account {account.ExternalAccountId}";
        }

        public string AccountDomain(CustomerAccount account)
        {
            if (canViewBusinessDetails)
            {
                return account.Domain;
            }

            MaskedFields.Add("account.domain");
            return $"sha256:{HashValue(account.Domain)[..12]}";
        }

        public string AccountLabel(CustomerAccount account)
            => canViewBusinessDetails ? account.Name : $"Account {account.ExternalAccountId}";

        public string SupportSubject(SupportTicket ticket)
        {
            if (canViewSensitiveDetails)
            {
                return ticket.Subject;
            }

            MaskedFields.Add("supportTicket.subject");
            return "[masked]";
        }

        public string SupportTicketSummary(SupportTicket ticket)
            => canViewSensitiveDetails
                ? $"{ticket.Severity} {ticket.Status} support ticket: {ticket.Subject}"
                : $"{ticket.Severity} {ticket.Status} support ticket in {ticket.Category}.";

        public string SalesActivitySummary(SalesActivity activity)
            => canViewSensitiveDetails
                ? activity.Summary
                : $"{activity.ActivityType} activity with outcome {activity.Outcome}.";

        public string Money(decimal amount, string field)
        {
            if (canViewBillingAmounts)
            {
                return amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            }

            MaskedFields.Add(field);
            return "[masked]";
        }

        public string PatternSubjectId(CustomerContact contact)
            => canViewPii ? contact.ExternalContactId : $"sha256:{HashValue(contact.ExternalContactId)[..12]}";

        public string PatternAccountId(CustomerAccount account)
            => canViewBusinessDetails ? account.ExternalAccountId : $"sha256:{HashValue(account.ExternalAccountId)[..12]}";

        public string MaskSubjectIdentifier(string subjectType, string identifier)
            => NormalizeSubjectType(subjectType) switch
            {
                "email" when !canViewPii => MaskEmail(identifier.Trim().ToLowerInvariant()),
                "account" when !canViewBusinessDetails => $"sha256:{HashValue(identifier)[..12]}",
                _ => identifier.Trim()
            };
    }
}

internal static class EnumerableExtensions
{
    public static IReadOnlyList<string> WhereNotNull(this IEnumerable<string?> values)
        => values.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value!).ToList();
}
