using PatientService.Models.Dtos;

namespace PatientService.Services;

public interface IPatientRegistrationService
{
    Task<RegisterPatientResult> RegisterPatientAsync(
        RegisterPatientRequest request,
        string correlationId,
        Guid actingUserId,
        CancellationToken cancellationToken = default);
}
