namespace NotificationService.Models.Events;

// NotificationService's own copy of MedicalRecordService's event contract. The event
// carries identifiers, diagnosis and the original UTC completion time.
public sealed class ConsultationCompletedEvent
{
    public Guid EventId { get; init; }
    public Guid ConsultationId { get; init; }
    public Guid QueueId { get; init; }
    public Guid PatientId { get; init; }
    public Guid DoctorId { get; init; }

    // Absent on events published before the diagnosis was added to the contract.
    public string? Diagnosis { get; init; }
    public DateTime? CompletedAt { get; init; }
}
