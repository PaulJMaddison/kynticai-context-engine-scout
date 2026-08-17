using KynticAI.Scout.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KynticAI.Scout.Infrastructure.Jobs;

internal sealed class ContextRecomputeWorker(
    ContextRecomputeQueue queue,
    IServiceScopeFactory scopeFactory,
    IBackgroundJobMonitor backgroundJobMonitor,
    IClock clock,
    ILogger<ContextRecomputeWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        backgroundJobMonitor.ReportHeartbeat("context-recompute-worker", true, "Worker started.", clock.UtcNow);
        try
        {
            await foreach (var request in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    backgroundJobMonitor.ReportHeartbeat("context-recompute-worker", true, $"Processing {request.CorrelationId}.", clock.UtcNow);
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<ContextRecomputeProcessor>();
                    await processor.ProcessAsync(request, stoppingToken);
                    backgroundJobMonitor.ReportHeartbeat("context-recompute-worker", true, $"Completed {request.CorrelationId}.", clock.UtcNow);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    backgroundJobMonitor.ReportHeartbeat("context-recompute-worker", true, "Worker stopping with interrupted work left recoverable.", clock.UtcNow);
                    return;
                }
                catch (Exception ex)
                {
                    backgroundJobMonitor.ReportHeartbeat("context-recompute-worker", false, ex.Message, clock.UtcNow);
                    logger.LogError(ex, "Failed to process context recompute request {CorrelationId}", request.CorrelationId);
                }
                finally
                {
                    queue.Complete(request);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            backgroundJobMonitor.ReportHeartbeat("context-recompute-worker", true, "Worker stopped.", clock.UtcNow);
        }
    }
}
