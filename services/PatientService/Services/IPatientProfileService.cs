using PatientService.Models.Dtos;

namespace PatientService.Services;

public interface IPatientProfileService
{
    // Null return means the patient does not exist or is soft-deleted - the controller
    // maps that to 404.
    Task<PatientProfileResponse?> GetPatientAsync(Guid patientId, CancellationToken cancellationToken = default);
}
