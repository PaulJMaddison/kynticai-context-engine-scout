using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;

namespace KynticAI.Scout.UnitTests;

public sealed class ConnectorCaptureOwnershipTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ConnectorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Epoch = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime SnapshotCompletedAt =
        DateTime.SpecifyKind(new DateTime(2026, 8, 16, 1, 0, 0), DateTimeKind.Utc);
    private static readonly string HighWaterHash = new('a', 64);
    private static readonly string TokenHash = new('b', 64);

    private static ConnectorCaptureOwnership Create()
        => ConnectorCaptureOwnership.CreateForCutover(
            TenantId,
            ConnectorId,
            selectedGeneration: 7,
            SnapshotCompletedAt,
            HighWaterHash,
            Epoch,
            TokenHash,
            SnapshotCompletedAt.AddMinutes(1));

    [Fact]
    public void HappyPath_RequiresScoutPauseBeforeFortressOwnsSource()
    {
        var ownership = Create();
        Assert.Equal(ConnectorCaptureOwnershipState.ScoutActive, ownership.State);
        Assert.True(ownership.ScoutMayCapture);
        Assert.False(ownership.FortressMayCapture);

        ownership.PauseScoutForCutover(SnapshotCompletedAt.AddMinutes(2));
        Assert.Equal(ConnectorCaptureOwnershipState.ScoutPausedForCutover, ownership.State);
        Assert.False(ownership.ScoutMayCapture);
        Assert.False(ownership.FortressMayCapture);
        Assert.NotNull(ownership.ScoutPausedAtUtc);

        ownership.TransferToFortress(Epoch, TokenHash, SnapshotCompletedAt.AddMinutes(3));
        Assert.Equal(ConnectorCaptureOwnershipState.FortressOwned, ownership.State);
        Assert.False(ownership.ScoutMayCapture);
        Assert.True(ownership.FortressMayCapture);
        Assert.NotNull(ownership.FortressOwnedAtUtc);
    }

    [Fact]
    public void CannotJumpDirectlyFromScoutActiveToFortressOwned()
    {
        var ownership = Create();
        Assert.Throws<InvalidOperationException>(() =>
            ownership.TransferToFortress(Epoch, TokenHash, SnapshotCompletedAt.AddMinutes(2)));
        Assert.True(ownership.ScoutMayCapture);
    }

    [Fact]
    public void WrongEpochOrTokenFailsClosed()
    {
        var ownership = Create();
        ownership.PauseScoutForCutover(SnapshotCompletedAt.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(() =>
            ownership.TransferToFortress(Guid.NewGuid(), TokenHash, SnapshotCompletedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() =>
            ownership.TransferToFortress(Epoch, new string('c', 64), SnapshotCompletedAt.AddMinutes(3)));
        Assert.Equal(ConnectorCaptureOwnershipState.ScoutPausedForCutover, ownership.State);
    }

    [Fact]
    public void AbortedCutoverCanRebindToFreshGenerationAndEpoch()
    {
        var ownership = Create();
        ownership.PauseScoutForCutover(SnapshotCompletedAt.AddMinutes(2));
        ownership.ResumeScoutAfterAbortedCutover(SnapshotCompletedAt.AddMinutes(3));
        Assert.Equal(ConnectorCaptureOwnershipState.ScoutActive, ownership.State);
        Assert.True(ownership.ScoutMayCapture);

        var nextCompleted = SnapshotCompletedAt.AddHours(1);
        var nextEpoch = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var nextHighWater = new string('c', 64);
        var nextToken = new string('d', 64);
        ownership.RebindForCutover(
            8,
            nextCompleted,
            nextHighWater,
            nextEpoch,
            nextToken,
            nextCompleted.AddMinutes(1));
        ownership.AssertBinding(8, nextCompleted, nextHighWater, nextEpoch, nextToken);

        ownership.PauseScoutForCutover(nextCompleted.AddMinutes(2));
        ownership.TransferToFortress(nextEpoch, nextToken, nextCompleted.AddMinutes(3));
        Assert.True(ownership.FortressMayCapture);
        Assert.Throws<InvalidOperationException>(() =>
            ownership.ResumeScoutAfterAbortedCutover(nextCompleted.AddMinutes(4)));
        Assert.Throws<InvalidOperationException>(() =>
            ownership.RebindForCutover(
                9,
                nextCompleted.AddHours(1),
                new string('e', 64),
                Guid.NewGuid(),
                new string('f', 64),
                nextCompleted.AddHours(1).AddMinutes(1)));
    }

    [Fact]
    public void BindingIncludesGenerationCompletionHighWaterEpochAndToken()
    {
        var ownership = Create();
        ownership.AssertBinding(7, SnapshotCompletedAt, HighWaterHash, Epoch, TokenHash);

        Assert.Throws<InvalidOperationException>(() =>
            ownership.AssertBinding(8, SnapshotCompletedAt, HighWaterHash, Epoch, TokenHash));
        Assert.Throws<InvalidOperationException>(() =>
            ownership.AssertBinding(7, SnapshotCompletedAt.AddSeconds(1), HighWaterHash, Epoch, TokenHash));
        Assert.Throws<InvalidOperationException>(() =>
            ownership.AssertBinding(7, SnapshotCompletedAt, new string('c', 64), Epoch, TokenHash));
    }

    [Fact]
    public void InvalidBarrierMetadataIsRejectedBeforeStateExists()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConnectorCaptureOwnership.CreateForCutover(
                TenantId,
                ConnectorId,
                0,
                SnapshotCompletedAt,
                HighWaterHash,
                Epoch,
                TokenHash,
                SnapshotCompletedAt.AddMinutes(1)));

        Assert.Throws<ArgumentException>(() =>
            ConnectorCaptureOwnership.CreateForCutover(
                TenantId,
                ConnectorId,
                7,
                SnapshotCompletedAt,
                "not-a-hash",
                Epoch,
                TokenHash,
                SnapshotCompletedAt.AddMinutes(1)));
    }
}
