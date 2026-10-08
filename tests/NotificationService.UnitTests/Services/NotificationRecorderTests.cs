using Microsoft.EntityFrameworkCore;
using NotificationService.Models.Enums;
using NotificationService.Services;

namespace NotificationService.UnitTests.Services;

// SWC-143: an event is stored once, however many times Kafka delivers it.
public class NotificationRecorderTests
{
    [Fact]
    public async Task NewEventIsStored()
    {
        await using var database = await TestDatabase.CreateAsync();
        var notification = TestDatabase.NewNotification(type: NotificationType.PatientCalled);

        var outcome = await new NotificationRecorder(database.DbContext).RecordAsync(notification);

        Assert.Equal(RecordNotificationOutcome.Recorded, outcome);
        await using var reader = database.NewContext();
        var stored = await reader.Notifications.SingleAsync();
        Assert.Equal(notification.Id, stored.Id);
        Assert.Equal(notification.EventId, stored.EventId);
        Assert.Equal(NotificationType.PatientCalled, stored.Type);
        Assert.Equal(notification.PatientId, stored.PatientId);
        Assert.Equal("Q-007", stored.QueueNumber);
        Assert.Equal("Dr. Silva", stored.DoctorName);
        Assert.Equal("1", stored.RoomNumber);
        Assert.Equal(notification.OccurredAt, stored.OccurredAt);
    }

    [Fact]
    public async Task EventDeliveredAgainIsIgnoredAndTheFirstCopyIsKept()
    {
        await using var database = await TestDatabase.CreateAsync();
        var eventId = Guid.NewGuid();
        var first = TestDatabase.NewNotification(eventId);
        await database.SeedAsync(first);

        var outcome = await new NotificationRecorder(database.DbContext)
            .RecordAsync(TestDatabase.NewNotification(eventId));

        Assert.Equal(RecordNotificationOutcome.Duplicate, outcome);
        await using var reader = database.NewContext();
        Assert.Equal(first.Id, (await reader.Notifications.SingleAsync()).Id);
    }

    [Fact]
    public async Task DifferentEventsAreAllStored()
    {
        await using var database = await TestDatabase.CreateAsync();
        var recorder = new NotificationRecorder(database.DbContext);

        await recorder.RecordAsync(TestDatabase.NewNotification());
        await recorder.RecordAsync(TestDatabase.NewNotification(type: NotificationType.ConsultationCompleted));

        await using var reader = database.NewContext();
        Assert.Equal(2, await reader.Notifications.CountAsync());
    }

    // Two instances can both pass the duplicate check; the unique index stops the second.
    [Fact]
    public async Task EventStoredByAnotherInstanceDuringTheSaveIsReportedAsADuplicate()
    {
        var eventId = Guid.NewGuid();
        await using var database = await TestDatabase.CreateAsync(
            beforeSave: other => other.SeedAsync(TestDatabase.NewNotification(eventId)));

        var outcome = await new NotificationRecorder(database.DbContext)
            .RecordAsync(TestDatabase.NewNotification(eventId));

        Assert.Equal(RecordNotificationOutcome.Duplicate, outcome);
        await using var reader = database.NewContext();
        Assert.Single(await reader.Notifications.ToListAsync());
    }

    // After a lost race the context must be usable for the next event.
    [Fact]
    public async Task RecorderStillWorksAfterALostRace()
    {
        var eventId = Guid.NewGuid();
        var raced = false;
        await using var database = await TestDatabase.CreateAsync(beforeSave: async other =>
        {
            if (!raced)
            {
                raced = true;
                await other.SeedAsync(TestDatabase.NewNotification(eventId));
            }
        });
        var recorder = new NotificationRecorder(database.DbContext);
        await recorder.RecordAsync(TestDatabase.NewNotification(eventId));

        var next = await recorder.RecordAsync(TestDatabase.NewNotification());

        Assert.Equal(RecordNotificationOutcome.Recorded, next);
        await using var reader = database.NewContext();
        Assert.Equal(2, await reader.Notifications.CountAsync());
    }

    [Fact]
    public async Task SaveFailureThatIsNotADuplicateIsRethrown()
    {
        await using var database = await TestDatabase.CreateAsync(
            beforeSave: _ => throw new DbUpdateException("Simulated database failure"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            new NotificationRecorder(database.DbContext).RecordAsync(TestDatabase.NewNotification()));

        Assert.Equal("Simulated database failure", exception.Message);
    }

    [Fact]
    public async Task MissingNotificationIsRejected()
    {
        await using var database = await TestDatabase.CreateAsync();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new NotificationRecorder(database.DbContext).RecordAsync(null!));

        Assert.Equal("notification", exception.ParamName);
    }
}
