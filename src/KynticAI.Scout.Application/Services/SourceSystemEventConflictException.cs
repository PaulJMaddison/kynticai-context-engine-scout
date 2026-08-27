namespace KynticAI.Scout.Application.Services;

public sealed class SourceSystemEventConflictException(
    string sourceSystem,
    string eventId)
    : InvalidOperationException(
        $"Source event '{sourceSystem}/{eventId}' already exists with different content. " +
        "Event identifiers are immutable and cannot be reused for a different event.")
{
    public string SourceSystem { get; } = sourceSystem;

    public string EventId { get; } = eventId;
}
