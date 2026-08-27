using System.Text.Json;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.Infrastructure.ReferenceData;

/// <summary>
/// Adapter for the fictional CustomerOps database used by LocalDemo/reference
/// scenarios. This provider is intentionally not registered in production
/// unless an operator explicitly enables the reference-data feature.
/// </summary>
internal sealed class CustomerOpsOperationalReferenceDataProvider(
    CustomerOpsDbContext dbContext,
    IClock clock)
    : IOperationalReferenceDataProvider
{
    public bool IsEnabled => true;

    public async Task<OperationalAccountReferenceResult?> GetAccountAsync(
        string tenantSlug,
        string externalAccountId,
        CancellationToken cancellationToken)
    {
        var normalizedTenant = tenantSlug.Trim().ToLowerInvariant();
        var normalizedAccount = externalAccountId.Trim();
        var tenant = await dbContext.CustomerOpsTenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == normalizedTenant, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        var account = await dbContext.CustomerAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.CustomerOpsTenantId == tenant.Id
                    && x.ExternalAccountId == normalizedAccount,
                cancellationToken);
        if (account is null)
        {
            return null;
        }

        var contacts = await dbContext.CustomerContacts
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == tenant.Id && x.CustomerAccountId == account.Id)
            .OrderByDescending(x => x.IsDecisionMaker)
            .ThenBy(x => x.FullName)
            .Select(x => new OperationalContactReferenceResult(
                x.ExternalUserId,
                x.FullName,
                x.Email,
                x.JobTitle))
            .ToListAsync(cancellationToken);

        return new OperationalAccountReferenceResult(
            account.ExternalAccountId,
            account.Name,
            account.Domain,
            account.Industry,
            account.Segment,
            account.Region,
            account.LifecycleStage,
            contacts);
    }

    public async Task<string?> ResolveExternalUserIdByAccountAsync(
        string tenantSlug,
        string externalAccountId,
        CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(tenantSlug, externalAccountId, cancellationToken);
        return account?.Contacts.FirstOrDefault()?.ExternalUserId;
    }

    public async Task<OperationalSourceSummaryResult?> GetSourceSummaryAsync(
        string tenantSlug,
        string externalUserId,
        bool canViewSensitivePii,
        CancellationToken cancellationToken)
    {
        var normalizedTenant = tenantSlug.Trim().ToLowerInvariant();
        var tenant = await dbContext.CustomerOpsTenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == normalizedTenant, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        var contact = await dbContext.CustomerContacts
            .AsNoTracking()
            .Include(x => x.Account)
            .FirstOrDefaultAsync(
                x => x.CustomerOpsTenantId == tenant.Id && x.ExternalUserId == externalUserId.Trim(),
                cancellationToken);
        if (contact is null)
        {
            return null;
        }

        var accountId = contact.CustomerAccountId;
        var now = clock.UtcNow;

        var latestSubscription = await dbContext.CustomerSubscriptions
            .AsNoTracking()
            .Include(x => x.ProductPlan)
            .Where(x => x.CustomerOpsTenantId == tenant.Id && x.CustomerAccountId == accountId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var latestBilling = await dbContext.BillingMetrics
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == tenant.Id && x.CustomerAccountId == accountId)
            .OrderByDescending(x => x.MetricDateUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var latestUsage = await dbContext.ProductUsageSummaries
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == tenant.Id && x.CustomerContactId == contact.Id)
            .OrderByDescending(x => x.SummaryDateUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var openOpportunities = await dbContext.SalesOpportunities.CountAsync(
            x => x.CustomerOpsTenantId == tenant.Id && x.CustomerAccountId == accountId && x.IsOpen,
            cancellationToken);
        var openSupportTickets = await dbContext.SupportTickets.CountAsync(
            x => x.CustomerOpsTenantId == tenant.Id
                && x.CustomerAccountId == accountId
                && x.Status != "resolved"
                && x.Status != "closed",
            cancellationToken);
        var pricingPageVisits30d = await dbContext.WebConversionEvents.CountAsync(
            x => x.CustomerOpsTenantId == tenant.Id
                && x.CustomerContactId == contact.Id
                && x.Page == "pricing"
                && x.OccurredAtUtc >= now.AddDays(-30),
            cancellationToken);
        var emailReplies30d = await dbContext.EmailEngagementEvents.CountAsync(
            x => x.CustomerOpsTenantId == tenant.Id
                && x.CustomerContactId == contact.Id
                && (x.EventType == "reply" || x.EventType == "meeting_booked")
                && x.OccurredAtUtc >= now.AddDays(-30),
            cancellationToken);

        var recentActivities = await dbContext.SalesActivities
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == tenant.Id && x.CustomerAccountId == accountId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(3)
            .Select(x => new OperationalTimelineEventResult("sales-activity", x.Summary, x.OccurredAtUtc))
            .ToListAsync(cancellationToken);
        var recentSupport = await dbContext.SupportTickets
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == tenant.Id && x.CustomerAccountId == accountId)
            .OrderByDescending(x => x.OpenedAtUtc)
            .Take(2)
            .Select(x => new OperationalTimelineEventResult("support-ticket", $"{x.Severity}: {x.Subject}", x.OpenedAtUtc))
            .ToListAsync(cancellationToken);
        var recentConversions = await dbContext.WebConversionEvents
            .AsNoTracking()
            .Where(x => x.CustomerOpsTenantId == tenant.Id && x.CustomerContactId == contact.Id)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(3)
            .Select(x => new OperationalTimelineEventResult("web-conversion", $"{x.EventType} on {x.Page}", x.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        var highlights = new List<OperationalHighlightResult>
        {
            new("Open opportunities", openOpportunities.ToString(), "Fictional demo pipeline attached to this account."),
            new("Pricing visits (30d)", pricingPageVisits30d.ToString(), "Fictional demo pricing-page activity."),
            new("Email replies (30d)", emailReplies30d.ToString(), "Fictional demo reply activity."),
            new("Active days (30d)", latestUsage?.ActiveDays30.ToString() ?? "0", "Fictional demo usage activity."),
            new("Open support tickets", openSupportTickets.ToString(), "Fictional demo support activity.")
        };

        var rawSummaryJson = JsonSerializer.Serialize(new
        {
            referenceData = true,
            account = new
            {
                contact.Account.ExternalAccountId,
                contact.Account.Name,
                contact.Account.Domain,
                contact.Account.Industry,
                contact.Account.Region,
                contact.Account.Segment,
                contact.Account.LifecycleStage
            },
            contact = new
            {
                contact.ExternalContactId,
                contact.ExternalUserId,
                contact.FullName,
                Email = canViewSensitivePii ? contact.Email : MaskEmail(contact.Email),
                contact.JobTitle
            },
            subscription = latestSubscription is null
                ? null
                : new
                {
                    latestSubscription.Status,
                    latestSubscription.MonthlyRecurringRevenue,
                    activePlan = latestSubscription.ProductPlan.Name
                },
            billing = latestBilling,
            latestUsage,
            counters = new
            {
                openOpportunities,
                openSupportTickets,
                pricingPageVisits30d,
                emailReplies30d
            }
        });

        return new OperationalSourceSummaryResult(
            contact.Account.ExternalAccountId,
            contact.Account.Name,
            contact.Account.Domain,
            contact.Account.Industry,
            contact.Account.Region,
            contact.Account.LifecycleStage,
            latestSubscription?.ProductPlan.Name ?? "No active plan",
            latestSubscription?.Status ?? "none",
            latestBilling?.MonthlyRecurringRevenue ?? latestSubscription?.MonthlyRecurringRevenue ?? 0m,
            openOpportunities,
            openSupportTickets,
            pricingPageVisits30d,
            latestUsage?.ActiveDays30 ?? 0,
            emailReplies30d,
            highlights,
            recentActivities
                .Concat(recentSupport)
                .Concat(recentConversions)
                .OrderByDescending(x => x.OccurredAtUtc)
                .Take(8)
                .ToList(),
            rawSummaryJson);
    }

    private static string MaskEmail(string email)
    {
        var separator = email.IndexOf('@', StringComparison.Ordinal);
        if (separator <= 1)
        {
            return "***";
        }

        return $"{email[0]}***{email[separator..]}";
    }
}
