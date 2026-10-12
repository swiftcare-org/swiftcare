using MedicalRecordService.Models.Entities;

namespace MedicalRecordService.Data;

public interface IConsultationHistoryRepository
{
    Task<IReadOnlyList<Consultation>> ListCompletedAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    Task<Consultation?> FindLatestCompletedAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);
}
