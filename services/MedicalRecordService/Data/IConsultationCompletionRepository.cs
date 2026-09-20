using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Data;

public interface IConsultationCompletionRepository
{
    Task<CompletionPreparationResult> PrepareAsync(
        Guid consultationId,
        Guid doctorId,
        CancellationToken cancellationToken = default);
}
