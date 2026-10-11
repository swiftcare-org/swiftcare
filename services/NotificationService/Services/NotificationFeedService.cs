using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationService.Data;
using NotificationService.Models.Configuration;
using NotificationService.Models.Dtos;
using NotificationService.Models.Entities;

namespace NotificationService.Services;

public sealed class NotificationFeedService(
    NotificationDbContext dbContext,
    IOptions<ReportOptions> options,
    TimeProvider timeProvider) : INotificationFeedService
{
    private readonly ClinicCalendar _calendar = new(options.Value.ClinicTimeZone);
    public const int DefaultLimit = 50;
    public const int MaximumLimit = 200;

    public Task<IReadOnlyList<NotificationResponse>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        ReadAsync(dbContext.Notifications.AsNoTracking(), limit, cancellationToken);

    public Task<IReadOnlyList<NotificationResponse>> GetTodayAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var today = _calendar.DateOf(timeProvider.GetUtcNow().UtcDateTime);
        var fromUtc = _calendar.StartOfDayUtc(today);
        var toUtc = _calendar.StartOfDayUtc(today.AddDays(1));
        return ReadAsync(dbContext.Notifications.AsNoTracking()
            .Where(notification => notification.OccurredAt >= fromUtc && notification.OccurredAt < toUtc),
            limit, cancellationToken);
    }

    private static async Task<IReadOnlyList<NotificationResponse>> ReadAsync(
        IQueryable<Notification> query, int limit, CancellationToken cancellationToken)
    {
        var notifications = await query
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
