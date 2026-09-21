namespace MedicalRecordService.Models.Dtos;

public sealed record OverdueFollowUpResponse(
    Guid ConsultationId,
    DateOnly FollowUpDate,
    string Instructions);
