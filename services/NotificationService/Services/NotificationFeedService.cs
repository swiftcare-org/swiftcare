using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Models.Dtos;
using NotificationService.Models.Entities;

namespace NotificationService.Services;

public sealed class NotificationFeedService(NotificationDbContext dbContext) : INotificationFeedService
{
    public const int DefaultLimit = 50;
    public const int MaximumLimit = 200;

    public async Task<IReadOnlyList<NotificationResponse>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var notifications = await dbContext.Notifications
            .AsNoTracking()
            .OrderByDescending(notification => notification.OccurredAt)
            // Events that share a timestamp keep a stable order between two polls.
            .ThenByDescending(notification => notification.ReceivedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(Math.Clamp(limit, 1, MaximumLimit))
            .ToListAsync(cancellationToken);

        return notifications.Select(ToResponse).ToArray();
    }

    private static NotificationResponse ToResponse(Notification notification) => new(
        notification.Id,
        notification.Type.ToString(),
        notification.PatientId,
        // Stored in UTC but read back without a kind. Marking it makes the JSON carry a
        // "Z", so the browser does not mistake it for local time.
        DateTime.SpecifyKind(notification.OccurredAt, DateTimeKind.Utc),
        notification.IsNewPatient,
        notification.QueueNumber,
        notification.DoctorName,
        notification.RoomNumber);
}
