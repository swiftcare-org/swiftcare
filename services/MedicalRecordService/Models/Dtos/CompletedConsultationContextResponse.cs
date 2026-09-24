namespace MedicalRecordService.Models.Dtos;

public sealed record CompletedConsultationContextResponse(
    Guid ConsultationId,
    Guid QueueId,
    Guid PatientId);
