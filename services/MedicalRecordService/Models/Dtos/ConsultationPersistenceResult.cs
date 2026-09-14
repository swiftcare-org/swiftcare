using MedicalRecordService.Models.Enums;

namespace MedicalRecordService.Models.Dtos;

public sealed class ConsultationPersistenceResult
{
    public required ConsultationPersistenceOutcome Outcome { get; init; }
    public string? TemplateName { get; init; }
}
