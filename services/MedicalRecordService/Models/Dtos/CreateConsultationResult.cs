using MedicalRecordService.Models.Enums;

namespace MedicalRecordService.Models.Dtos;

public sealed class CreateConsultationResult
{
    public required CreateConsultationOutcome Outcome { get; init; }
    public ConsultationResponse? Consultation { get; init; }
}
