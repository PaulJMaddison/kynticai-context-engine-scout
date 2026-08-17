using System.Threading.Channels;
using KynticAI.Scout.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace KynticAI.Scout.Infrastructure.Jobs;

internal sealed class ContextRecomputeQueue : IContextRecomputeQueue
{
    private const int DefaultCapacity = 1_024;
    private const int MaximumCapacity = 100_000;

    private readonly IBackgroundJobMonitor backgroundJobMonitor;
    private readonly Channel<ContextRecomputeRequest> channel;
    private int pendingCount;

    public ContextRecomputeQueue(IBackgroundJobMonitor backgroundJobMonitor, IConfiguration configuration)
    {
        this.backgroundJobMonitor = backgroundJobMonitor;
        var configuredCapacity = configuration.GetValue<int?>("BackgroundJobs:ContextRecomputeQueueCapacity") ?? DefaultCapacity;
        var capacity = Math.Clamp(configuredCapacity, 1, MaximumCapacity);
        channel = Channel.CreateBounded<ContextRecomputeRequest>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        backgroundJobMonitor.UpdateQueueDepth("context-recompute-queue", 0);
    }

    public async ValueTask EnqueueAsync(ContextRecomputeRequest request, CancellationToken cancellationToken)
    {
        await channel.Writer.WriteAsync(request, cancellationToken);
        backgroundJobMonitor.UpdateQueueDepth("context-recompute-queue", Interlocked.Increment(ref pendingCount));
    }

    public async IAsyncEnumerable<ContextRecomputeRequest> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var request in channel.Reader.ReadAllAsync(cancellationToken))
        {
            backgroundJobMonitor.UpdateQueueDepth("context-recompute-queue", Math.Max(0, Interlocked.Decrement(ref pendingCount)));
            yield return request;
        }
    }
}
