namespace QueueService.Models.Events;

// Must match the identifier-only event published by MedicalRecordService.
public sealed record ConsultationCompletedEvent(
    Guid EventId,
    Guid ConsultationId,
    Guid QueueId,
    Guid PatientId,
    Guid DoctorId);
