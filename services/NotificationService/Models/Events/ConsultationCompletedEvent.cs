namespace NotificationService.Models.Events;

// NotificationService's own copy of MedicalRecordService's event contract. The event
// carries identifiers only, and no timestamp.
public sealed class ConsultationCompletedEvent
{
    public Guid EventId { get; init; }
    public Guid ConsultationId { get; init; }
    public Guid QueueId { get; init; }
    public Guid PatientId { get; init; }
    public Guid DoctorId { get; init; }
}
