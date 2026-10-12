using Microsoft.Extensions.Options;
using NotificationService.Models.Configuration;
using NotificationService.Services;

namespace NotificationService.UnitTests.Services;

public sealed class TodaysNotificationFeedTests
{
    private static readonly DateTime Start = new(2026, 10, 10, 18, 30, 0, DateTimeKind.Utc);

    private static NotificationFeedService Create(TestDatabase database, Clock clock, string zone = "Asia/Colombo") =>
        new(database.DbContext, Options.Create(new ReportOptions { ClinicTimeZone = zone }), clock);

    [Theory]
    [InlineData("Asia/Colombo", 18, 30)]
    [InlineData("UTC", 0, 0)]
    public async Task ClinicDayIncludesItsStartAndExcludesTheNextMidnight(string zone, int hour, int minute)
    {
        await using var database = await TestDatabase.CreateAsync();
        var start = new DateTime(2026, 10, 10, hour, minute, 0, DateTimeKind.Utc);
        var first = TestDatabase.NewNotification(occurredAt: start);
        var last = TestDatabase.NewNotification(occurredAt: start.AddDays(1).AddTicks(-1));
        await database.SeedAsync(TestDatabase.NewNotification(occurredAt: start.AddTicks(-1)),
            first, last, TestDatabase.NewNotification(occurredAt: start.AddDays(1)));

        var feed = await Create(database, new Clock(start.AddHours(2)), zone).GetTodayAsync(50);

        Assert.Equal(new[] { last.Id, first.Id }, feed.Select(entry => entry.Id));
        Assert.All(feed, entry => Assert.Equal(DateTimeKind.Utc, entry.OccurredAt.Kind));
        Assert.Empty(database.DbContext.ChangeTracker.Entries());
    }

    [Fact]
    public async Task MidnightRolloverUsesTheNewDayOnTheNextPoll()
    {
        await using var database = await TestDatabase.CreateAsync();
        var previous = TestDatabase.NewNotification(occurredAt: Start.AddHours(1));
        var next = TestDatabase.NewNotification(occurredAt: Start.AddDays(1));
        await database.SeedAsync(previous, next);
        var clock = new Clock(Start.AddDays(1).AddTicks(-1));
        var service = Create(database, clock);

        Assert.Equal(previous.Id, Assert.Single(await service.GetTodayAsync(50)).Id);
        clock.Now = new DateTimeOffset(Start.AddDays(1));
        Assert.Equal(next.Id, Assert.Single(await service.GetTodayAsync(50)).Id);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(100_000, 3)]
    public async Task FilteringPrecedesTheLimitAndEqualTimesKeepStableNewestFirstOrder(int limit, int expected)
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = TestDatabase.NewNotification(occurredAt: Start, receivedAt: Start.AddSeconds(1));
        var second = TestDatabase.NewNotification(occurredAt: Start, receivedAt: Start.AddSeconds(2),
            id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var third = TestDatabase.NewNotification(occurredAt: Start, receivedAt: Start.AddSeconds(2),
            id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        await database.SeedAsync(first, second, third, TestDatabase.NewNotification(occurredAt: Start.AddDays(1)));

        var feed = await Create(database, new Clock(Start.AddHours(2))).GetTodayAsync(limit);

        Assert.Equal(new[] { third.Id, second.Id, first.Id }.Take(expected), feed.Select(entry => entry.Id));
    }

    [Fact]
    public async Task ActivityOutsideTodayProducesAnEmptyTodayFeedButRemainsInAllActivity()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(TestDatabase.NewNotification(occurredAt: Start.AddMinutes(-1)),
            TestDatabase.NewNotification(occurredAt: Start.AddDays(1)));
        var service = Create(database, new Clock(Start.AddHours(2)));

        Assert.Empty(await service.GetTodayAsync(50));
        Assert.Equal(2, (await service.GetRecentAsync(50)).Count);
    }

    [Fact]
    public async Task CancelledRequestsDoNotContinueReadingTheFeed()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(database, new Clock(Start)).GetTodayAsync(50, cancellation.Token));
    }

    private sealed class Clock(DateTime utc) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(utc);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
