namespace QueueService.Models.Events;

// Contains only the identifiers and assignment details required by downstream queue and
// consultation workflows. Patient demographic or contact details remain in PatientService.
public sealed class PatientCalledEvent
{
    public required Guid EventId { get; init; }
    public required Guid QueueId { get; init; }
    public required Guid PatientId { get; init; }
    public required string QueueNumber { get; init; }
    public required Guid DoctorId { get; init; }
    public required string DoctorName { get; init; }
    public required string RoomNumber { get; init; }
    public required DateTime CalledAt { get; init; }
    public required string CorrelationId { get; init; }
}
