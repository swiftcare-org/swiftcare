namespace MedicalRecordService.Models.Entities;

public sealed record ConsultationFollowUp(
    Guid ConsultationId,
    DateOnly? FollowUpDate,
    string? Instructions);
