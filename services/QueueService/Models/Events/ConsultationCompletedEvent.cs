namespace QueueService.Models.Events;

// Consumes the visit identifiers and optional completion time published by MedicalRecordService.
public sealed record ConsultationCompletedEvent(
    Guid EventId,
    Guid ConsultationId,
    Guid QueueId,
    Guid PatientId,
    Guid DoctorId,
    DateTime? CompletedAt = null);
