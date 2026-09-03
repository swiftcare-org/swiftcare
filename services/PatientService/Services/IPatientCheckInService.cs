using PatientService.Models.Enums;

namespace PatientService.Services;

public interface IPatientCheckInService
{
    Task<CheckInPatientOutcome> CheckInPatientAsync(
        Guid patientId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
