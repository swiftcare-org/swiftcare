using PatientService.Models.Enums;

namespace PatientService.Models.Dtos;

// Deliberately narrower than the Patient entity: a receptionist identifying an arriving
// patient needs only these four fields plus the ID. Date of birth, address, and gender are
// PHI the search screen has no use for, so they never leave the database.
public sealed class PatientSearchResultResponse
{
    public required Guid PatientId { get; init; }
    public required string FullName { get; init; }
    public required string Nic { get; init; }
    public required string PhoneNumber { get; init; }
    public required BloodGroup BloodGroup { get; init; }
}
