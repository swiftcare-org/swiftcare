namespace MedicalRecordService.Models.Dtos;

public sealed record ConsultationProgressResponse(
    Guid Id,
    Guid QueueId,
    string Status,
    bool HasVitalSigns);
