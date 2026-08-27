using KynticAI.Scout.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.Application.Abstractions;

public interface ICustomerOpsDbContext
{
    DbSet<CustomerOpsTenant> CustomerOpsTenants { get; }

    DbSet<CustomerAccount> CustomerAccounts { get; }

    DbSet<CustomerContact> CustomerContacts { get; }

    DbSet<CustomerUser> CustomerUsers { get; }

    DbSet<ProductCatalogItem> ProductCatalogItems { get; }

    DbSet<ProductPlan> ProductPlans { get; }

    DbSet<CustomerSubscription> CustomerSubscriptions { get; }

    DbSet<SalesOpportunity> SalesOpportunities { get; }

    DbSet<SalesActivity> SalesActivities { get; }

    DbSet<EmailEngagementEvent> EmailEngagementEvents { get; }

    DbSet<SupportTicket> SupportTickets { get; }

    DbSet<ProductUsageSummary> ProductUsageSummaries { get; }

    DbSet<BillingMetric> BillingMetrics { get; }

    DbSet<WebConversionEvent> WebConversionEvents { get; }

    DbSet<CustomerContactSignal> CustomerContactSignals { get; }

    DbSet<CustomerEmailSignal> CustomerEmailSignals { get; }

    DbSet<CustomerContextRollup> CustomerContextRollups { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
