namespace PatientService.Models.Dtos;

// Minimal by design: the receptionist's own form already has the name and other fields
// they just submitted, so the response returns only what's needed to confirm the record
// was created - the patient ID. No PHI is echoed back.
public sealed class RegisteredPatientResponse
{
    public required Guid PatientId { get; init; }
    public required DateTime CreatedAt { get; init; }
}
