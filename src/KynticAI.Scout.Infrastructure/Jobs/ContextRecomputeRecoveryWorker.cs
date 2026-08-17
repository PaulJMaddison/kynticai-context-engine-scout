using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KynticAI.Scout.Infrastructure.Jobs;

/// <summary>
/// Re-enqueues persisted recompute jobs that were committed but never delivered to the in-memory
/// channel, or were interrupted while the process was shutting down. The processor is idempotent
/// for terminal jobs and already-succeeded selector executions, so recovery does not repeat work
/// that was durably completed before the interruption.
/// </summary>
internal sealed class ContextRecomputeRecoveryWorker(
    ContextRecomputeQueue queue,
    IServiceScopeFactory scopeFactory,
    IBackgroundJobMonitor backgroundJobMonitor,
    IClock clock,
    IConfiguration configuration,
    ILogger<ContextRecomputeRecoveryWorker> logger)
    : BackgroundService
{
    private const int MaximumJobsPerScan = 250;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue<int?>("BackgroundJobs:RecoveryPollSeconds") ?? 15,
            5,
            300));

        backgroundJobMonitor.ReportHeartbeat("context-recompute-recovery", true, "Recovery worker started.", clock.UtcNow);
        await RecoverDueJobsAsync(stoppingToken);

        using var timer = new PeriodicTimer(pollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RecoverDueJobsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            backgroundJobMonitor.ReportHeartbeat("context-recompute-recovery", true, "Recovery worker stopped.", clock.UtcNow);
        }
    }

    private async Task RecoverDueJobsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = clock.UtcNow;
            var pendingDelay = TimeSpan.FromSeconds(Math.Clamp(
                configuration.GetValue<int?>("BackgroundJobs:PendingRecoverySeconds") ?? 15,
                1,
                300));
            var runningDelay = TimeSpan.FromMinutes(Math.Clamp(
                configuration.GetValue<int?>("BackgroundJobs:RunningRecoveryMinutes") ?? 5,
                1,
                120));
            var pendingCutoff = now - pendingDelay;
            var runningCutoff = now - runningDelay;

            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
            var jobs = await dbContext.RecomputeJobs
                .AsNoTracking()
                .Where(job =>
                    (job.Status == RecomputeJobStatus.Pending && job.RequestedAtUtc <= pendingCutoff)
                    || (job.Status == RecomputeJobStatus.Running
                        && (!job.StartedAtUtc.HasValue || job.StartedAtUtc <= runningCutoff)))
                .OrderBy(job => job.RequestedAtUtc)
                .Take(MaximumJobsPerScan)
                .Select(job => new
                {
                    job.TenantId,
                    job.UserProfileId,
                    job.CorrelationId
                })
                .ToListAsync(cancellationToken);

            var recovered = 0;
            foreach (var job in jobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var executionIds = await dbContext.SelectorExecutions
                    .AsNoTracking()
                    .Where(execution => execution.TenantId == job.TenantId
                        && execution.UserProfileId == job.UserProfileId
                        && execution.CorrelationId == job.CorrelationId)
                    .OrderBy(execution => execution.RequestedAtUtc)
                    .Select(execution => execution.Id)
                    .ToListAsync(cancellationToken);

                await queue.EnqueueAsync(
                    new ContextRecomputeRequest(job.TenantId, job.UserProfileId, job.CorrelationId, executionIds),
                    cancellationToken);
                recovered++;
            }

            backgroundJobMonitor.ReportHeartbeat(
                "context-recompute-recovery",
                true,
                recovered == 0 ? "No stranded recompute jobs found." : $"Re-enqueued {recovered} recoverable recompute job(s).",
                now);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            backgroundJobMonitor.ReportHeartbeat("context-recompute-recovery", false, ex.Message, clock.UtcNow);
            logger.LogError(ex, "Failed to scan for recoverable recompute jobs.");
        }
    }
}
