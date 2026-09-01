using PatientService.Models.Enums;

namespace PatientService.Models.Dtos;

public sealed class AllergyResponse
{
    public required Guid AllergyId { get; init; }
    public required string AllergyName { get; init; }
    public required AllergySeverity Severity { get; init; }
    public string? Notes { get; init; }

    // Projects the entity's CreatedAt - the "date recorded" shown in the allergies list.
    public required DateTime RecordedAt { get; init; }
}
