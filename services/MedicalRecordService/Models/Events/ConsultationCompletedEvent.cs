namespace MedicalRecordService.Models.Events;

// Identifiers plus the diagnosis, which the department reports count. Clinical notes,
// symptoms and patient demographics do not belong on this topic.
public sealed record ConsultationCompletedEvent(
    Guid EventId,
    Guid ConsultationId,
    Guid QueueId,
    Guid PatientId,
    Guid DoctorId,
    string Diagnosis,
    DateTime? CompletedAt = null);
