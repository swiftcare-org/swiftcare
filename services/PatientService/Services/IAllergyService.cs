using PatientService.Models.Dtos;

namespace PatientService.Services;

public interface IAllergyService
{
    // Null return means the patient does not exist (or is soft-deleted) - the controller
    // maps that to 404. An empty, non-null list means the patient exists but has no
    // recorded allergies.
    Task<IReadOnlyList<AllergyResponse>?> GetAllergiesAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    Task<AllergyResponse?> AddAllergyAsync(
        Guid patientId,
        AllergyRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    Task<AllergyResponse?> UpdateAllergyAsync(
        Guid patientId,
        Guid allergyId,
        AllergyRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAllergyAsync(
        Guid patientId,
        Guid allergyId,
        Guid actingUserId,
        CancellationToken cancellationToken = default);
}
