namespace PatientService.Models.Events;

// Deliberately minimal: no name, NIC, date of birth, address, phone, or blood group.
// Consumers that need patient detail must call PatientService's own API for it - the
// event is only the async signal that a check-in happened, not a data-replication channel.
public sealed class PatientCheckedInEvent
{
    public required Guid EventId { get; init; }
    public required Guid PatientId { get; init; }
    public required bool IsNewPatient { get; init; }
    public required DateTime CheckedInAt { get; init; }
    public required string CorrelationId { get; init; }
}
