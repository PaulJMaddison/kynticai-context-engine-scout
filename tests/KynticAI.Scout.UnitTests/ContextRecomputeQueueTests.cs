using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Infrastructure.Jobs;
using Microsoft.Extensions.Configuration;

namespace KynticAI.Scout.UnitTests;

public sealed class ContextRecomputeQueueTests
{
    [Fact]
    public async Task BoundedQueue_AppliesBackpressure_AndTracksDepthAcrossReaderRace()
    {
        var monitor = new InMemoryBackgroundJobMonitor();
        var queue = CreateQueue(monitor, capacity: 1);
        var first = Request("first");
        var second = Request("second");

        await queue.EnqueueAsync(first, CancellationToken.None);
        var secondWrite = queue.EnqueueAsync(second, CancellationToken.None).AsTask();

        Assert.False(secondWrite.IsCompleted);
        Assert.Equal(2, QueueDepth(monitor));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = queue.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(first, reader.Current);

        await secondWrite.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, QueueDepth(monitor));

        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(second, reader.Current);
        Assert.Equal(0, QueueDepth(monitor));
    }

    [Fact]
    public async Task BoundedQueue_CancelledBlockedWriter_DoesNotLeakQueueDepth()
    {
        var monitor = new InMemoryBackgroundJobMonitor();
        var queue = CreateQueue(monitor, capacity: 1);
        await queue.EnqueueAsync(Request("first"), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();

        var blockedWrite = queue.EnqueueAsync(Request("second"), cancellation.Token).AsTask();
        Assert.Equal(2, QueueDepth(monitor));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blockedWrite);
        Assert.Equal(1, QueueDepth(monitor));
    }

    private static ContextRecomputeQueue CreateQueue(InMemoryBackgroundJobMonitor monitor, int capacity)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BackgroundJobs:ContextRecomputeQueueCapacity"] = capacity.ToString()
            })
            .Build();
        return new ContextRecomputeQueue(monitor, configuration);
    }

    private static int QueueDepth(InMemoryBackgroundJobMonitor monitor) =>
        monitor.GetWorkers().Single(x => x.WorkerName == "context-recompute-queue").QueueDepth;

    private static ContextRecomputeRequest Request(string correlationId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), correlationId, [Guid.NewGuid()]);
}
