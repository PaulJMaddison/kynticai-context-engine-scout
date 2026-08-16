namespace KynticAI.Scout.Domain.Enums;

/// <summary>
/// Persistent ownership state for one connector during Scout -> Fortress cutover.
/// The database mapping is intentionally added separately with the EF migration so this domain
/// state machine can be reviewed and tested without hand-authoring migration metadata.
/// </summary>
public enum ConnectorCaptureOwnershipState
{
    ScoutActive = 0,
    ScoutPausedForCutover = 1,
    FortressOwned = 2
}
