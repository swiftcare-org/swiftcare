using QueueService.Models.Events;

namespace QueueService.Services;

public interface IQueueEventPublisher
{
    Task<bool> PublishPatientCalledAsync(
        PatientCalledEvent patientCalledEvent,
        CancellationToken cancellationToken = default);
}
