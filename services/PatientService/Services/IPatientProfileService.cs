using PatientService.Models.Dtos;

namespace PatientService.Services;

public interface IPatientProfileService
{
    // Null return means the patient does not exist or is soft-deleted - the controller
    // maps that to 404.
    Task<PatientProfileResponse?> GetPatientAsync(Guid patientId, CancellationToken cancellationToken = default);

    // Null return means the patient does not exist or is soft-deleted. The update request
    // intentionally exposes only the three mutable profile fields.
    Task<PatientProfileResponse?> UpdatePatientAsync(
        Guid patientId,
        UpdatePatientRequest request,
        CancellationToken cancellationToken = default);
}
