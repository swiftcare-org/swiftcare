using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Models.Entities;

namespace NotificationService.Services;

public sealed class NotificationRecorder(NotificationDbContext dbContext) : INotificationRecorder
{
    public async Task<RecordNotificationOutcome> RecordAsync(
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // Kafka delivers at least once, so the same event can arrive again after a restart
        // or a failed offset commit. It must not appear in the feed twice.
        if (await IsStoredAsync(notification.EventId, cancellationToken))
        {
            return RecordNotificationOutcome.Duplicate;
        }

        dbContext.Notifications.Add(notification);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another instance can store the same event between the check above and this
            // save. The unique index on EventId rejects the second insert.
            dbContext.Entry(notification).State = EntityState.Detached;
            if (!await IsStoredAsync(notification.EventId, cancellationToken))
            {
                throw;
            }

            return RecordNotificationOutcome.Duplicate;
        }

        return RecordNotificationOutcome.Recorded;
    }

    private Task<bool> IsStoredAsync(Guid eventId, CancellationToken cancellationToken) =>
        dbContext.Notifications
            .AsNoTracking()
            .AnyAsync(stored => stored.EventId == eventId, cancellationToken);
}
