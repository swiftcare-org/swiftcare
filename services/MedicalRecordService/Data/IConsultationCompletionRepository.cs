using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Data;

public interface IConsultationCompletionRepository
{
    Task<ConsultationProgressResponse?> FindByQueueAsync(
        Guid queueId,
        Guid doctorId,
        CancellationToken cancellationToken = default);

    Task<CompletionPreparationResult> PrepareAsync(
        Guid consultationId,
        Guid doctorId,
        CancellationToken cancellationToken = default);
}
