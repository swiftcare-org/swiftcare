using System.ComponentModel.DataAnnotations;
using PatientService.Models.Enums;

namespace PatientService.Models.Dtos;

public sealed class AllergyRequest
{
    // Exact copy required by SWC-17 Scenario 2 - deliberately without a trailing period,
    // unlike this DTO's other messages, to match the acceptance criteria verbatim.
    [Required(ErrorMessage = "Allergy name is required")]
    [StringLength(128, ErrorMessage = "Allergy name must be 128 characters or fewer.")]
    public string AllergyName { get; set; } = string.Empty;

    // Nullable so a missing severity is a validation error rather than silently binding to
    // Severe, matching the Gender/BloodGroup pattern in RegisterPatientRequest.
    [Required(ErrorMessage = "Severity is required.")]
    [EnumDataType(typeof(AllergySeverity), ErrorMessage = "Severity must be Severe, Moderate, or Mild.")]
    public AllergySeverity? Severity { get; set; }

    [StringLength(512, ErrorMessage = "Notes must be 512 characters or fewer.")]
    public string? Notes { get; set; }
}
