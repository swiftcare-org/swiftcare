using System.ComponentModel.DataAnnotations;

namespace PrescriptionService.Models.Dtos;

public sealed class CreatePrescriptionRequest : IValidatableObject
{
    public Guid ConsultationId { get; init; }
    public Guid QueueId { get; init; }
    public Guid PatientId { get; init; }

    [Required(ErrorMessage = "Add at least one medicine")]
    [MinLength(1, ErrorMessage = "Add at least one medicine")]
    public List<PrescriptionItemRequest> Medicines { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ConsultationId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Consultation ID is required",
                [nameof(ConsultationId)]);
        }

        if (QueueId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Queue ID is required",
                [nameof(QueueId)]);
        }

        if (PatientId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Patient ID is required",
                [nameof(PatientId)]);
        }
    }
}

public sealed class PrescriptionItemRequest
{
    [Required(ErrorMessage = "Medicine name is required")]
    [StringLength(200, ErrorMessage = "Medicine name must be 200 characters or fewer")]
    public string MedicineName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Dosage is required")]
    [StringLength(100, ErrorMessage = "Dosage must be 100 characters or fewer")]
    public string Dosage { get; init; } = string.Empty;

    [Required(ErrorMessage = "Frequency is required")]
    [StringLength(100, ErrorMessage = "Frequency must be 100 characters or fewer")]
    public string Frequency { get; init; } = string.Empty;

    [Required(ErrorMessage = "Duration is required")]
    [StringLength(100, ErrorMessage = "Duration must be 100 characters or fewer")]
    public string Duration { get; init; } = string.Empty;

    [StringLength(500, ErrorMessage = "Instructions must be 500 characters or fewer")]
    public string? Instructions { get; init; }
}
