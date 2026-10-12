namespace PatientService.Models.Entities;

// Kept separate from the Patient navigation graph so unrelated patient queries cannot
// accidentally load clinical condition data. ConditionService validates PatientId
// explicitly before creating or returning records.
public sealed class ChronicCondition : IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid PatientId { get; set; }
    public required string ConditionName { get; set; }
    public DateOnly DateDiagnosed { get; set; }
    public string? Notes { get; set; }

    // Removal hides the condition from active clinical views while retaining its audit trail.
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
