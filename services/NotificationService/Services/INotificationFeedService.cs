using NotificationService.Models.Dtos;

namespace NotificationService.Services;

public interface INotificationFeedService
{
    Task<IReadOnlyList<NotificationResponse>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
