using NotificationService.Models.Entities;

namespace NotificationService.Services;

public enum RecordNotificationOutcome
{
    Recorded,
    Duplicate
}

public interface INotificationRecorder
{
    Task<RecordNotificationOutcome> RecordAsync(
        Notification notification,
        CancellationToken cancellationToken = default);
}
