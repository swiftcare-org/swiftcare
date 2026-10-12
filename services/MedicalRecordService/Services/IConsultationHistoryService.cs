using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Services;

public interface IConsultationHistoryService
{
    Task<IReadOnlyList<ConsultationResponse>> GetHistoryAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    Task<ConsultationResponse?> GetLatestAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);
}
