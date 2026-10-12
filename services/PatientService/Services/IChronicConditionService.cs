using PatientService.Models.Dtos;

namespace PatientService.Services;

public interface IChronicConditionService
{
    // Null means the patient does not exist; an empty list means the patient exists but
    // has no active chronic conditions.
    Task<IReadOnlyList<ChronicConditionResponse>?> GetConditionsAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    Task<ChronicConditionResponse?> AddConditionAsync(
        Guid patientId,
        ChronicConditionRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveConditionAsync(
        Guid patientId,
        Guid conditionId,
        Guid actingUserId,
        CancellationToken cancellationToken = default);
}
