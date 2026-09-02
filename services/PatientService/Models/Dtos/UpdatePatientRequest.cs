using System.ComponentModel.DataAnnotations;
using PatientService.Models.Enums;

namespace PatientService.Models.Dtos;

// Only fields that may change after registration are part of this contract. NIC and
// DateOfBirth are deliberately absent, so neither normal clients nor over-posted JSON can
// mutate the protected identity fields through the profile endpoint.
public sealed class UpdatePatientRequest
{
    [Required(ErrorMessage = "Address is required.")]
    [StringLength(256, ErrorMessage = "Address must be 256 characters or fewer.")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    [StringLength(16, ErrorMessage = "Phone number must be 16 characters or fewer.")]
    [RegularExpression(@"^(0[0-9]{9}|\+94[0-9]{9})$", ErrorMessage = "Phone number must be a valid Sri Lankan number.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Blood group is required.")]
    [EnumDataType(typeof(BloodGroup), ErrorMessage = "Blood group must be one of A+, A-, B+, B-, O+, O-, AB+, AB-.")]
    public BloodGroup? BloodGroup { get; set; }
}
