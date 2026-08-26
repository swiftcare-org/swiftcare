using System.ComponentModel.DataAnnotations;
using PatientService.Models.Enums;
using PatientService.Models.Validation;

namespace PatientService.Models.Dtos;

public sealed class RegisterPatientRequest
{
    // Sri Lankan NIC: 9 digits + V/X (pre-2016 format) or 12 digits (current format).
    // Normalized (trimmed, uppercased) by PatientRegistrationService before the
    // uniqueness check and the insert - this attribute validates the raw input shape only.
    [Required(ErrorMessage = "NIC is required.")]
    [StringLength(12, ErrorMessage = "NIC must be 12 characters or fewer.")]
    [RegularExpression(@"^([0-9]{9}[VvXx]|[0-9]{12})$", ErrorMessage = "NIC must be 9 digits followed by V/X, or 12 digits.")]
    public string Nic { get; set; } = string.Empty;

    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(128, ErrorMessage = "Full name must be 128 characters or fewer.")]
    public string FullName { get; set; } = string.Empty;

    // Nullable so a missing date of birth is a validation error rather than silently
    // binding to default(DateOnly), matching the Role pattern in AuthService's
    // CreateUserRequest.
    [Required(ErrorMessage = "Date of birth is required.")]
    [PastDate(ErrorMessage = "Date of birth must be a valid past date.")]
    public DateOnly? DateOfBirth { get; set; }

    [Required(ErrorMessage = "Gender is required.")]
    [EnumDataType(typeof(Gender), ErrorMessage = "Gender must be Male, Female, or Other.")]
    public Gender? Gender { get; set; }

    [Required(ErrorMessage = "Address is required.")]
    [StringLength(256, ErrorMessage = "Address must be 256 characters or fewer.")]
    public string Address { get; set; } = string.Empty;

    // Any Sri Lankan number, local (0XXXXXXXXX) or international (+94XXXXXXXXX) format,
    // mobile or landline.
    [Required(ErrorMessage = "Phone number is required.")]
    [StringLength(16, ErrorMessage = "Phone number must be 16 characters or fewer.")]
    [RegularExpression(@"^(0[0-9]{9}|\+94[0-9]{9})$", ErrorMessage = "Phone number must be a valid Sri Lankan number.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Blood group is required.")]
    [EnumDataType(typeof(BloodGroup), ErrorMessage = "Blood group must be one of A+, A-, B+, B-, O+, O-, AB+, AB-.")]
    public BloodGroup? BloodGroup { get; set; }
}
