using PatientService.Models.Enums;

namespace PatientService.Models.Dtos;

public sealed class RegisterPatientResult
{
    public required RegisterPatientOutcome Outcome { get; init; }
    public RegisteredPatientResponse? Patient { get; init; }
}
