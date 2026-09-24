using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Services;

public interface IConsultationCompletionService
{
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
