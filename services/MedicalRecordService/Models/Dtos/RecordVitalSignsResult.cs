using MedicalRecordService.Models.Enums;

namespace MedicalRecordService.Models.Dtos;

public sealed class RecordVitalSignsResult
{
    public required RecordVitalSignsOutcome Outcome { get; init; }
    public VitalSignsResponse? VitalSigns { get; init; }
}
