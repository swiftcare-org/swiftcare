using PatientService.Models.Enums;

namespace PatientService.Models.Entities;

// Deliberately no navigation property on Patient: this keeps the existing entity untouched
// and makes it impossible to accidentally eager-load a patient's full allergy set (PHI)
// from an unrelated query. PatientId is validated against the Patients table explicitly in
// AllergyService rather than relying on the FK, since EF Core InMemory (used in tests)
// does not enforce foreign keys.
public sealed class Allergy : IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid PatientId { get; set; }
    public required string AllergyName { get; set; }
    public AllergySeverity Severity { get; set; }
    public string? Notes { get; set; }

    // Soft-deleted, never hard-deleted, to preserve the clinical audit trail.
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
