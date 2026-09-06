using System.ComponentModel.DataAnnotations;
using PatientService.Models.Validation;

namespace PatientService.Models.Dtos;

public sealed class ChronicConditionRequest
{
    [Required(ErrorMessage = "Condition name is required")]
    [StringLength(128, ErrorMessage = "Condition name must be 128 characters or fewer.")]
    public string ConditionName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Diagnosed date is required.")]
    [NotFutureClinicDate(ErrorMessage = "Diagnosed date cannot be in the future")]
    public DateOnly? DateDiagnosed { get; set; }

    [StringLength(512, ErrorMessage = "Notes must be 512 characters or fewer.")]
    public string? Notes { get; set; }
}
