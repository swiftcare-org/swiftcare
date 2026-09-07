namespace PatientService.Models.Dtos;

public sealed class ChronicConditionResponse
{
    public required Guid ConditionId { get; init; }
    public required string ConditionName { get; init; }
    public required DateOnly DateDiagnosed { get; init; }
    public string? Notes { get; init; }
}
