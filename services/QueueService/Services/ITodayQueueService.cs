using QueueService.Models.Dtos;

namespace QueueService.Services;

public interface ITodayQueueService
{
    Task<IReadOnlyList<TodayQueueEntryResponse>> GetTodayAsync(
        CancellationToken cancellationToken = default);
}
