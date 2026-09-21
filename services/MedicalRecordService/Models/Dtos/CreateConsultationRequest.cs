using System.ComponentModel.DataAnnotations;
using MedicalRecordService.Validation;

namespace MedicalRecordService.Models.Dtos;

public sealed class CreateConsultationRequest : IValidatableObject
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

    public DateOnly? FollowUpDate { get; init; }

    [StringLength(500, ErrorMessage = "Follow-up instructions must be 500 characters or fewer")]
    public string? FollowUpInstructions { get; init; }

    public Guid? TemplateId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasDate = FollowUpDate.HasValue;
        var hasInstructions = !string.IsNullOrWhiteSpace(FollowUpInstructions);

        if (hasDate == hasInstructions)
        {
            yield break;
        }

        if (!hasDate)
        {
            yield return new ValidationResult(
                "Follow-up date is required when follow-up instructions are provided",
                [nameof(FollowUpDate)]);
        }

        if (!hasInstructions)
        {
            yield return new ValidationResult(
                "Follow-up instructions are required when a follow-up date is provided",
                [nameof(FollowUpInstructions)]);
        }
    }
}
