using MedicalRecordService.Validation;

namespace MedicalRecordService.Models.Dtos;

public sealed class CreateConsultationRequest
{
    [NotEmptyGuid(ErrorMessage = "Queue ID is required")]
    public Guid QueueId { get; init; }

    [NotEmptyGuid(ErrorMessage = "Patient ID is required")]
    public Guid PatientId { get; init; }

    [NotWhiteSpace(ErrorMessage = "Symptoms are required")]
    public string Symptoms { get; init; } = string.Empty;

    public string? ExaminationFindings { get; init; }

    [NotWhiteSpace(ErrorMessage = "Diagnosis is required")]
    public string Diagnosis { get; init; } = string.Empty;

    public string? Notes { get; init; }
    public Guid? TemplateId { get; init; }
}
