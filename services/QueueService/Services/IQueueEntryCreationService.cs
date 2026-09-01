using QueueService.Models.Dtos;

namespace QueueService.Services;

public interface IQueueEntryCreationService
{
    Task<QueueEntryCreationResult> CreateQueueEntryAsync(
        Guid eventId,
        Guid patientId,
        DateTime checkedInAtUtc,
        CancellationToken cancellationToken = default);
}
