using MedicalRecordService.Models.Events;

namespace MedicalRecordService.Services;

public interface IConsultationCompletedPublisher
{
    Task<bool> PublishAsync(
        ConsultationCompletedEvent completedEvent,
        CancellationToken cancellationToken = default);
}
