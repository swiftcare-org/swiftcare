using PatientService.Models.Enums;

namespace PatientService.Models.Entities;

public sealed class Patient : IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Stored normalized (trimmed, uppercased) by PatientRegistrationService before every
    // read and write, so the unique index below catches case/whitespace variants of the
    // same NIC. The index is deliberately not filtered by !IsDeleted: a soft-deleted
    // patient must permanently keep its NIC, matching AuthService's Username index.
    public required string Nic { get; set; }

    public required string FullName { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public required string Address { get; set; }
    public required string PhoneNumber { get; set; }
    public BloodGroup BloodGroup { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
