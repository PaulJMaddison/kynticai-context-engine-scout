using System.Text.Json;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.Infrastructure.Jobs;

internal sealed class ScheduledRecomputeDispatcher(
    ScoutDbContext dbContext,
    IClock clock,
    IContextRecomputeQueue recomputeQueue)
    : IScheduledRecomputeDispatcher
{
    public async Task<ScheduledRecomputeDispatchResult> DispatchDueUsersAsync(string? tenantSlug, CancellationToken cancellationToken)
    {
        var publishedSelectors = await dbContext.SelectorDefinitions
            .AsNoTracking()
            .Include(x => x.Tenant)
            .Where(x => x.Status == SelectorStatus.Published && x.ScheduleIntervalMinutes.HasValue)
            .Where(x => tenantSlug == null || x.Tenant.Slug == tenantSlug.Trim().ToLowerInvariant())
            .OrderBy(x => x.TenantId)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        if (publishedSelectors.Count == 0)
        {
            return new ScheduledRecomputeDispatchResult(0, 0);
        }

        var queuedUsers = 0;
        var skippedUsers = 0;
        var utcNow = clock.UtcNow;
        var selectorsByTenant = publishedSelectors.GroupBy(x => x.TenantId);

        foreach (var tenantSelectors in selectorsByTenant)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tenantId = tenantSelectors.Key;
            var minScheduleIntervalMinutes = tenantSelectors.Min(x => x.ScheduleIntervalMinutes!.Value);
            var tenantPublishedSelectors = await dbContext.SelectorDefinitions
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Status == SelectorStatus.Published)
                .OrderBy(x => x.Name)
                .ToListAsync(cancellationToken);
            if (tenantPublishedSelectors.Count == 0)
            {
                continue;
            }

            var users = await dbContext.UserProfiles
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId)
                .ToListAsync(cancellationToken);

            foreach (var user in users)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Recovery owns stale/interrupted non-terminal jobs. Do not create a second
                // scheduled recompute just because the original has been running for a while.
                var hasPendingExecution = await dbContext.SelectorExecutions.AnyAsync(
                    x => x.TenantId == tenantId
                         && x.UserProfileId == user.Id
                         && (x.Status == SelectorExecutionStatus.Pending || x.Status == SelectorExecutionStatus.Running),
                    cancellationToken);

                if (hasPendingExecution)
                {
                    skippedUsers++;
                    continue;
                }

                var latestSnapshot = await dbContext.ContextSnapshots
                    .AsNoTracking()
                    .Include(x => x.Facts)
                    .Where(x => x.TenantId == tenantId && x.UserProfileId == user.Id)
                    .OrderByDescending(x => x.GeneratedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                var dueByAge = latestSnapshot is null || latestSnapshot.GeneratedAtUtc <= utcNow.AddMinutes(-minScheduleIntervalMinutes);
                var dueByStaleness = latestSnapshot?.Facts.Any(fact => fact.FreshUntilUtc.HasValue && fact.FreshUntilUtc.Value < utcNow) ?? true;
                if (!dueByAge && !dueByStaleness)
                {
                    skippedUsers++;
                    continue;
                }

                var correlationId = "schedule-" + Guid.NewGuid().ToString("N");
                var executions = tenantPublishedSelectors
                    .Select(selector => SelectorExecution.Create(tenantId, selector.Id, user.Id, correlationId, "scheduler", SelectorExecutionMode.Scheduled, utcNow))
                    .ToList();
                var recomputeJob = RecomputeJob.Create(
                    tenantId,
                    user.Id,
                    correlationId,
                    "scheduler",
                    executions.Count,
                    $"Scheduled recompute requested for {user.ExternalUserId}.",
                    JsonSerializer.Serialize(new
                    {
                        mode = "scheduled",
                        user.ExternalUserId,
                        dueByAge,
                        dueByStaleness,
                        selectorCount = executions.Count
                    }),
                    utcNow);

                dbContext.SelectorExecutions.AddRange(executions);
                dbContext.RecomputeJobs.Add(recomputeJob);
                dbContext.AuditEvents.Add(AuditEvent.Create(
                    tenantId,
                    "scheduler",
                    "context.recompute.scheduled",
                    nameof(UserProfile),
                    user.Id.ToString("D"),
                    correlationId,
                    JsonSerializer.Serialize(new
                    {
                        user.ExternalUserId,
                        dueByAge,
                        dueByStaleness,
                        executionCount = executions.Count
                    }),
                    null,
                    null,
                    utcNow));

                await dbContext.SaveChangesAsync(cancellationToken);
                await recomputeQueue.EnqueueAsync(new ContextRecomputeRequest(tenantId, user.Id, correlationId, executions.Select(x => x.Id).ToList()), cancellationToken);
                queuedUsers++;
            }
        }

        return new ScheduledRecomputeDispatchResult(queuedUsers, skippedUsers);
    }
}
