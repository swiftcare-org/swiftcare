using NotificationService.Models.Enums;
using NotificationService.Services;

namespace NotificationService.UnitTests.Services;

// SWC-143: the feed lists events newest first.
public class NotificationFeedServiceTests
{
    private static readonly DateTime BaseTime = new(2026, 10, 8, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EventsAreReturnedNewestFirst()
    {
        await using var database = await TestDatabase.CreateAsync();
        var oldest = TestDatabase.NewNotification(occurredAt: BaseTime.AddMinutes(1));
        var newest = TestDatabase.NewNotification(occurredAt: BaseTime.AddMinutes(3));
        var middle = TestDatabase.NewNotification(occurredAt: BaseTime.AddMinutes(2));
        await database.SeedAsync(oldest, newest, middle);

        var feed = await new NotificationFeedService(database.DbContext).GetRecentAsync(50);

        Assert.Equal(new[] { newest.Id, middle.Id, oldest.Id }, feed.Select(entry => entry.Id).ToArray());
    }

    [Fact]
    public async Task EventsAtTheSameTimeAreOrderedByArrivalNewestFirst()
    {
        await using var database = await TestDatabase.CreateAsync();
        var arrivedFirst = TestDatabase.NewNotification(occurredAt: BaseTime, receivedAt: BaseTime.AddSeconds(1));
        var arrivedLast = TestDatabase.NewNotification(occurredAt: BaseTime, receivedAt: BaseTime.AddSeconds(2));
        await database.SeedAsync(arrivedFirst, arrivedLast);

        var feed = await new NotificationFeedService(database.DbContext).GetRecentAsync(50);

        Assert.Equal(new[] { arrivedLast.Id, arrivedFirst.Id }, feed.Select(entry => entry.Id).ToArray());
    }

    [Fact]
    public async Task LimitKeepsOnlyTheNewestEvents()
    {
        await using var database = await TestDatabase.CreateAsync();
        var notifications = Enumerable.Range(1, 5)
            .Select(minute => TestDatabase.NewNotification(occurredAt: BaseTime.AddMinutes(minute)))
            .ToArray();
        await database.SeedAsync(notifications);

        var feed = await new NotificationFeedService(database.DbContext).GetRecentAsync(2);

        Assert.Equal(new[] { notifications[4].Id, notifications[3].Id }, feed.Select(entry => entry.Id).ToArray());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(1, 1)]
    [InlineData(NotificationFeedService.MaximumLimit, NotificationFeedService.MaximumLimit)]
    [InlineData(NotificationFeedService.MaximumLimit + 1, NotificationFeedService.MaximumLimit)]
    [InlineData(100_000, NotificationFeedService.MaximumLimit)]
    public async Task LimitIsClampedToTheSupportedRange(int requested, int expectedCount)
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(Enumerable.Range(0, NotificationFeedService.MaximumLimit + 5)
            .Select(second => TestDatabase.NewNotification(occurredAt: BaseTime.AddSeconds(second)))
            .ToArray());

        var feed = await new NotificationFeedService(database.DbContext).GetRecentAsync(requested);

        Assert.Equal(expectedCount, feed.Count);
    }

    [Fact]
    public void LimitsMatchTheDocumentedValues()
    {
        Assert.Equal(50, NotificationFeedService.DefaultLimit);
        Assert.Equal(200, NotificationFeedService.MaximumLimit);
    }

    [Fact]
    public async Task NoEventsGivesAnEmptyFeed()
    {
        await using var database = await TestDatabase.CreateAsync();

        Assert.Empty(await new NotificationFeedService(database.DbContext).GetRecentAsync(50));
    }

    [Fact]
    public async Task CheckInEntryCarriesWhetherThePatientIsNew()
    {
        await using var database = await TestDatabase.CreateAsync();
        var notification = TestDatabase.NewNotification(type: NotificationType.PatientCheckedIn, occurredAt: BaseTime);
        await database.SeedAsync(notification);

        var entry = Assert.Single(await new NotificationFeedService(database.DbContext).GetRecentAsync(50));

        Assert.Equal(notification.Id, entry.Id);
        Assert.Equal("PatientCheckedIn", entry.Type);
        Assert.Equal(notification.PatientId, entry.PatientId);
        Assert.True(entry.IsNewPatient);
        Assert.Null(entry.QueueNumber);
        Assert.Null(entry.DoctorName);
        Assert.Null(entry.RoomNumber);
    }

    [Fact]
    public async Task PatientCalledEntryCarriesQueueNumberDoctorAndRoom()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(TestDatabase.NewNotification(type: NotificationType.PatientCalled));

        var entry = Assert.Single(await new NotificationFeedService(database.DbContext).GetRecentAsync(50));

        Assert.Equal("PatientCalled", entry.Type);
        Assert.Equal("Q-007", entry.QueueNumber);
        Assert.Equal("Dr. Silva", entry.DoctorName);
        Assert.Equal("1", entry.RoomNumber);
        Assert.Null(entry.IsNewPatient);
    }

    [Fact]
    public async Task ConsultationCompletedEntryHasItsOwnType()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(TestDatabase.NewNotification(type: NotificationType.ConsultationCompleted));

        var entry = Assert.Single(await new NotificationFeedService(database.DbContext).GetRecentAsync(50));

        Assert.Equal("ConsultationCompleted", entry.Type);
    }

    // The browser reads a time without a zone as local time, so it must be marked as UTC.
    [Fact]
    public async Task EventTimeIsReturnedAsUtc()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(TestDatabase.NewNotification(occurredAt: BaseTime));

        var entry = Assert.Single(await new NotificationFeedService(database.DbContext).GetRecentAsync(50));

        Assert.Equal(DateTimeKind.Utc, entry.OccurredAt.Kind);
        Assert.Equal(BaseTime, entry.OccurredAt);
    }

    [Fact]
    public async Task ReadingTheFeedTracksAndChangesNothing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(TestDatabase.NewNotification());

        await new NotificationFeedService(database.DbContext).GetRecentAsync(50);

        Assert.Empty(database.DbContext.ChangeTracker.Entries());
    }
}
