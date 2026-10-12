using System.ComponentModel.DataAnnotations;
using MedicalRecordService.Validation;

namespace MedicalRecordService.Models.Dtos;

// There is deliberately no owner here: a template always belongs to the doctor the
// Gateway identified, never to an ID sent in the body.
public sealed class CreateConsultationTemplateRequest
{
    public const int NameMaxLength = 100;

    // The clinical text columns are MySQL TEXT (65,535 bytes). This stays inside that
    // limit even when every character needs four bytes.
    public const int ClinicalTextMaxLength = 5000;

    [NotWhiteSpace(ErrorMessage = "Template name is required")]
    [StringLength(NameMaxLength, ErrorMessage = "Template name must be 100 characters or fewer")]
    public string Name { get; init; } = string.Empty;

    [NotWhiteSpace(ErrorMessage = "Symptoms are required")]
    [StringLength(ClinicalTextMaxLength, ErrorMessage = "Symptoms must be 5000 characters or fewer")]
    public string Symptoms { get; init; } = string.Empty;

    [NotWhiteSpace(ErrorMessage = "Examination findings are required")]
    [StringLength(ClinicalTextMaxLength, ErrorMessage = "Examination findings must be 5000 characters or fewer")]
    public string ExaminationFindings { get; init; } = string.Empty;

    [NotWhiteSpace(ErrorMessage = "Notes are required")]
    [StringLength(ClinicalTextMaxLength, ErrorMessage = "Notes must be 5000 characters or fewer")]
    public string Notes { get; init; } = string.Empty;
}
