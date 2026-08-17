using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, byte> outstandingRequests = new(StringComparer.Ordinal);
    private int pendingCount;

    public ContextRecomputeQueue(IBackgroundJobMonitor backgroundJobMonitor)
        : this(backgroundJobMonitor, configuration: null)
    {
    }

    public ContextRecomputeQueue(IBackgroundJobMonitor backgroundJobMonitor, IConfiguration? configuration)
    {
        this.backgroundJobMonitor = backgroundJobMonitor;
        var configuredCapacity = configuration?.GetValue<int?>("BackgroundJobs:ContextRecomputeQueueCapacity") ?? DefaultCapacity;
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
        var key = RequestKey(request);
        if (!outstandingRequests.TryAdd(key, 0))
        {
            return;
        }

        var depth = Interlocked.Increment(ref pendingCount);
        backgroundJobMonitor.UpdateQueueDepth("context-recompute-queue", depth);
        try
        {
            await channel.Writer.WriteAsync(request, cancellationToken);
        }
        catch
        {
            outstandingRequests.TryRemove(key, out _);
            backgroundJobMonitor.UpdateQueueDepth(
                "context-recompute-queue",
                Math.Max(0, Interlocked.Decrement(ref pendingCount)));
            throw;
        }
    }

    public async IAsyncEnumerable<ContextRecomputeRequest> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var request in channel.Reader.ReadAllAsync(cancellationToken))
        {
            backgroundJobMonitor.UpdateQueueDepth("context-recompute-queue", Math.Max(0, Interlocked.Decrement(ref pendingCount)));
            yield return request;
        }
    }

    public void Complete(ContextRecomputeRequest request)
        => outstandingRequests.TryRemove(RequestKey(request), out _);

    private static string RequestKey(ContextRecomputeRequest request)
        => $"{request.TenantId:N}:{request.CorrelationId}";
}
