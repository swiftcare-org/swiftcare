using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Data;

public interface IConsultationCompletionRepository
{
    Task<IReadOnlyList<CompletedConsultationContextResponse>> FindCompletedPageAsync(
        Guid doctorId, int page, CancellationToken cancellationToken = default);

    Task<CompletedConsultationContextResponse?> FindLatestCompletedAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default);

    Task<ConsultationProgressResponse?> FindByQueueAsync(
        Guid queueId,
        Guid doctorId,
        CancellationToken cancellationToken = default);

    Task<CompletionPreparationResult> PrepareAsync(
        Guid consultationId,
        Guid doctorId,
        CancellationToken cancellationToken = default);
}
