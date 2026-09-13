namespace MedicalRecordService.Models.Dtos;

public sealed class ConsultationTemplateResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Symptoms { get; init; }
    public required string ExaminationFindings { get; init; }
    public required string Notes { get; init; }
}
