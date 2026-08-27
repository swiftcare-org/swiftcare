using PatientService.Models.Enums;

namespace PatientService.Models.Dtos;

public sealed class PatientProfileResponse
{
    public required Guid PatientId { get; init; }
    public required string FullName { get; init; }
    public required string Nic { get; init; }
    public required DateOnly DateOfBirth { get; init; }
    public required Gender Gender { get; init; }
    public required string Address { get; init; }
    public required string PhoneNumber { get; init; }
    public required BloodGroup BloodGroup { get; init; }
    public required DateTime RegisteredAt { get; init; }
}
