using QueueService.Models.Enums;
using QueueService.Models.Events;

namespace QueueService.Services;

public interface IQueueCompletionService
{
    Task<QueueCompletionOutcome> CompleteAsync(
        ConsultationCompletedEvent completedEvent,
        CancellationToken cancellationToken = default);
}
