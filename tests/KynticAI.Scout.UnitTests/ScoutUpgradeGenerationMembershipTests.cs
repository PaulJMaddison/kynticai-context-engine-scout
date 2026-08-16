using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Entities;

namespace KynticAI.Scout.UnitTests;

public sealed class ScoutUpgradeGenerationMembershipTests
{
    [Fact]
    public void GenerationMembershipRequiresPositiveGeneration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceCaptureGenerationMember.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            Guid.NewGuid(),
            "kyntic-connector:11111111-1111-1111-1111-111111111111",
            "contact",
            "c-1",
            DateTime.UtcNow));
    }

    [Fact]
    public void SameRetainedEventCanBelongToLaterSnapshotGeneration()
    {
        var tenant = Guid.NewGuid();
        var connector = Guid.NewGuid();
        var sourceEvent = Guid.NewGuid();
        var sourceNamespace = $"kyntic-connector:{connector:D}";
        var now = DateTime.UtcNow;

        var first = SourceCaptureGenerationMember.Create(
            tenant,
            connector,
            1,
            sourceEvent,
            sourceNamespace,
            "contact",
            "c-1",
            now);
        var second = SourceCaptureGenerationMember.Create(
            tenant,
            connector,
            2,
            sourceEvent,
            sourceNamespace,
            "contact",
            "c-1",
            now.AddMinutes(1));

        Assert.Equal(sourceEvent, first.SourceSystemEventId);
        Assert.Equal(sourceEvent, second.SourceSystemEventId);
        Assert.Equal(1, first.Generation);
        Assert.Equal(2, second.Generation);
        Assert.Equal(sourceNamespace, second.SourceNamespace);
    }

    [Fact]
    public void MissingGenerationMembershipProofFailsClosed()
    {
        var evidence = CompleteEvidence() with { GenerationMembershipKnown = false };

        Assert.Equal(
            LocalUpgradeReadiness.HistoryLimited,
            ScoutFortressUpgradePolicy.Classify(evidence));
    }

    [Fact]
    public void EmptyEstateNeedsGenerationMembershipProofToo()
    {
        var withoutMembership = CompleteEvidence() with
        {
            HasRetainedEvents = false,
            GenerationMembershipKnown = false
        };
        var withMembership = withoutMembership with { GenerationMembershipKnown = true };

        Assert.Equal(
            LocalUpgradeReadiness.HistoryLimited,
            ScoutFortressUpgradePolicy.Classify(withoutMembership));
        Assert.Equal(
            LocalUpgradeReadiness.LosslessDerivedRebuild,
            ScoutFortressUpgradePolicy.Classify(withMembership));
    }

    [Fact]
    public void LiveKeysetAndApiCursorAreNotPointInTimeClaims()
    {
        Assert.False(LocalDataPlaneContracts.IsStrongCurrentStateConsistency(
            LocalDataPlaneContracts.CurrentStateLiveKeyset));
        Assert.False(LocalDataPlaneContracts.IsStrongCurrentStateConsistency(
            LocalDataPlaneContracts.CurrentStateApiCursor));
        Assert.True(LocalDataPlaneContracts.IsStrongCurrentStateConsistency(
            LocalDataPlaneContracts.CurrentStateImmutableSnapshot));
        Assert.True(LocalDataPlaneContracts.IsStrongCurrentStateConsistency(
            LocalDataPlaneContracts.CurrentStatePointInTime));
    }

    private static UpgradeCompatibilityEvidence CompleteEvidence()
        => new(
            SupportedRelationalProvider: true,
            IsPostgres: true,
            HasConnectorInstallations: true,
            ConnectorCredentialsReferencedLocally: true,
            HasRetainedEvents: true,
            AllRetainedEventsHaveCaptureMetadata: true,
            AllRetainedEventsRetainFullPermittedPayload: true,
            ConnectorTypesSupportedByTarget: true,
            RequiresSourceReconnect: false,
            HistoricalCoverageKnownComplete: true,
            ExactPayloadEvidenceRetained: true,
            CurrentStateContinuityKnown: true,
            GenerationMembershipKnown: true);
}
