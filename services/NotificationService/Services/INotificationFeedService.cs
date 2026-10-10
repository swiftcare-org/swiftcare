using NotificationService.Models.Dtos;

namespace NotificationService.Services;

public interface INotificationFeedService
{
    Task<IReadOnlyList<NotificationResponse>> GetTodayAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationResponse>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
