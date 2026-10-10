using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Services;

public interface IConsultationCompletionService
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

    Task<CompleteConsultationResult> CompleteAsync(
        Guid consultationId,
        Guid doctorId,
        CancellationToken cancellationToken = default);
}
