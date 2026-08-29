namespace QueueService.Models.Events;

// QueueService's own copy of PatientService's event contract - not a shared project
// reference. If PatientService changes this shape, QueueService breaks visibly at
// deserialization (logged and skipped) rather than silently through a shared dependency.
public sealed class PatientCheckedInEvent
{
    public required Guid EventId { get; init; }
    public required Guid PatientId { get; init; }
    public required bool IsNewPatient { get; init; }
    public required DateTime CheckedInAt { get; init; }
    public required string CorrelationId { get; init; }
}
