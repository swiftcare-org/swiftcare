using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Services;

public interface IVitalSignsHistoryService
{
    Task<IReadOnlyList<VitalSignsResponse>> GetHistoryAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);
}
