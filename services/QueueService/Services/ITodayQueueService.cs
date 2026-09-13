using QueueService.Models.Dtos;

namespace QueueService.Services;

public interface ITodayQueueService
{
    Task<IReadOnlyList<TodayQueueEntryResponse>> GetTodayAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodayQueueEntryResponse>> GetWaitingAsync(
        CancellationToken cancellationToken = default);

    Task<WaitingRoomDisplayResponse> GetDisplayAsync(
        CancellationToken cancellationToken = default);
}
