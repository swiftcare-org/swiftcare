using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Services;

public interface IConsultationCompletionService
{
    Task<CompleteConsultationResult> CompleteAsync(
        Guid consultationId,
        Guid doctorId,
        CancellationToken cancellationToken = default);
}
