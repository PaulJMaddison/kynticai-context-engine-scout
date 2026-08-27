using KynticAI.Scout.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace KynticAI.Scout.Application.Abstractions;

public interface ICustomerOpsDbContext
{
    /// <summary>
    /// The underlying <see cref="DbContext.Database"/> facade, exposed so SQL connectors can
    /// route to a shared <c>customerOpsDatabase</c> connection without coupling to the concrete
    /// reference-store type. Only meaningful when a CustomerOps reference store is configured.
    /// </summary>
    DatabaseFacade Database { get; }

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
