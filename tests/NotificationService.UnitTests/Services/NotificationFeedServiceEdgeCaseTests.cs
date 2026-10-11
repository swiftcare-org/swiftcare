using NotificationService.Services;

namespace NotificationService.UnitTests.Services;

// The last tie-break of the feed order (SWC-151 mutation testing).
public class NotificationFeedServiceEdgeCaseTests
{
    private static readonly DateTime BaseTime = new(2026, 10, 8, 4, 0, 0, DateTimeKind.Utc);

    // Events that share both timestamps must come back in the same order on every poll.
    [Fact]
    public async Task EventsThatShareBothTimesAreOrderedByIdDescending()
    {
        await using var database = await TestDatabase.CreateAsync();
        var lowerId = TestDatabase.NewNotification(
            occurredAt: BaseTime,
            receivedAt: BaseTime,
            id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var higherId = TestDatabase.NewNotification(
            occurredAt: BaseTime,
            receivedAt: BaseTime,
            id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        await database.SeedAsync(lowerId, higherId);

        var feed = await new NotificationFeedService(database.DbContext).GetRecentAsync(50);

        Assert.Equal(new[] { higherId.Id, lowerId.Id }, feed.Select(entry => entry.Id).ToArray());
    }
}
