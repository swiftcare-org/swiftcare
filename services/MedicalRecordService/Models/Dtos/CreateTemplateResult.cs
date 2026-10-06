using MedicalRecordService.Models.Enums;

namespace MedicalRecordService.Models.Dtos;

public sealed class CreateTemplateResult
{
    public required CreateTemplateOutcome Outcome { get; init; }
    public ConsultationTemplateResponse? Template { get; init; }
}
