namespace MedicalRecordService.Models.Events;

// Identifiers only: clinical notes and patient demographics do not belong on this topic.
public sealed record ConsultationCompletedEvent(
    Guid EventId,
    Guid ConsultationId,
    Guid QueueId,
    Guid PatientId,
    Guid DoctorId);
