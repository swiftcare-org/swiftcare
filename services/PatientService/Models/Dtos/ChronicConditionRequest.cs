using System.ComponentModel.DataAnnotations;

namespace PatientService.Models.Dtos;

public sealed class ChronicConditionRequest : IValidatableObject
{
    [Required(ErrorMessage = "Condition name is required")]
    [StringLength(128, ErrorMessage = "Condition name must be 128 characters or fewer.")]
    public string ConditionName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Diagnosed date is required.")]
    public DateOnly? DateDiagnosed { get; set; }

    [StringLength(512, ErrorMessage = "Notes must be 512 characters or fewer.")]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateDiagnosed > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            yield return new ValidationResult(
                "Diagnosed date cannot be in the future",
                [nameof(DateDiagnosed)]);
        }
    }
}
