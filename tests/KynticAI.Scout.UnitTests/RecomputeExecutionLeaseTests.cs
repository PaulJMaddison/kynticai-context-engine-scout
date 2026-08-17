using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Infrastructure.Jobs;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.UnitTests;

public sealed class RecomputeExecutionLeaseTests
{
    [Fact]
    public async Task NonPostgresProvider_UsesAcquiredNoOpLease()
    {
        var options = new DbContextOptionsBuilder<ScoutDbContext>()
            .UseInMemoryDatabase($"recompute-lease-{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ScoutDbContext(options);
        var request = new ContextRecomputeRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "recovery-correlation",
            [Guid.NewGuid()]);

        await using var lease = await RecomputeExecutionLease.TryAcquireAsync(
            dbContext,
            request,
            CancellationToken.None);

        Assert.True(lease.Acquired);
    }
}
