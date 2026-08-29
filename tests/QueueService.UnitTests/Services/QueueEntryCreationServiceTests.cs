using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueueService.Data;
using QueueService.Models.Configuration;
using QueueService.Models.Entities;
using QueueService.Models.Enums;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

// EF Core InMemory (used by PatientService's tests) enforces neither unique indexes nor
// transactions, so it cannot honestly verify Scenarios 3/4 or the transactional counter
// this story's Definition of Done requires. SQLite is relational, enforces unique indexes,
// and supports transactions and concurrency tokens, so it is used here instead - each test
// opens its own in-memory SQLite connection, which must stay open for the schema to persist.
public class QueueEntryCreationServiceTests
{
    // Midday UTC, comfortably inside a single Asia/Colombo calendar day, so tests that
    // aren't specifically about the day boundary don't have to think about it.
    private static readonly DateTime FixedCheckedInAtUtc = new(2026, 8, 29, 6, 0, 0, DateTimeKind.Utc);

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static DbContextOptions<QueueDbContext> OptionsFor(SqliteConnection connection) =>
        new DbContextOptionsBuilder<QueueDbContext>().UseSqlite(connection).Options;

    private static QueueEntryCreationService CreateService(QueueDbContext dbContext, QueueOptions? options = null) =>
        new(dbContext, Options.Create(options ?? DefaultOptions()), NullLogger<QueueEntryCreationService>.Instance);

    private static QueueOptions DefaultOptions() => new()
    {
        ClinicTimeZone = "Asia/Colombo",
        MaxAllocationAttempts = 3
    };

    [Fact]
    public async Task CreateQueueEntry_ForThreeDistinctPatientsSameDay_AssignsSequentialNumbers()
    {
        using var connection = OpenConnection();
        var options = OptionsFor(connection);
        await using var dbContext = new QueueDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var service = CreateService(dbContext);

        var first = await service.CreateQueueEntryAsync(Guid.NewGuid(), Guid.NewGuid(), FixedCheckedInAtUtc);
        var second = await service.CreateQueueEntryAsync(Guid.NewGuid(), Guid.NewGuid(), FixedCheckedInAtUtc);
        var third = await service.CreateQueueEntryAsync(Guid.NewGuid(), Guid.NewGuid(), FixedCheckedInAtUtc);

        Assert.Equal("Q-001", first.QueueNumber);
        Assert.Equal("Q-002", second.QueueNumber);
        Assert.Equal("Q-003", third.QueueNumber);
        Assert.All(new[] { first, second, third }, r => Assert.Equal(QueueEntryCreationOutcome.Created, r.Outcome));
    }

    [Fact]
    public async Task CreateQueueEntry_CreatesEntryWithWaitingStatusAndNullRoomNumber()
    {
        using var connection = OpenConnection();
        var options = OptionsFor(connection);
        await using var dbContext = new QueueDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var service = CreateService(dbContext);
        await service.CreateQueueEntryAsync(Guid.NewGuid(), Guid.NewGuid(), FixedCheckedInAtUtc);

        var entry = await dbContext.QueueEntries.SingleAsync();
        Assert.Equal(QueueStatus.Waiting, entry.Status);
        Assert.Null(entry.RoomNumber);
    }

    [Fact]
    public async Task CreateQueueEntry_OnTheNextClinicDay_RestartsAtQ001AndLeavesPriorDayUntouched()
    {
        using var connection = OpenConnection();
        var options = OptionsFor(connection);
        await using var dbContext = new QueueDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var service = CreateService(dbContext);
        var day1 = FixedCheckedInAtUtc;
        var day2 = FixedCheckedInAtUtc.AddDays(1);

        var day1First = await service.CreateQueueEntryAsync(Guid.NewGuid(), Guid.NewGuid(), day1);
        var day1Second = await service.CreateQueueEntryAsync(Guid.NewGuid(), Guid.NewGuid(), day1);
        var day2First = await service.CreateQueueEntryAsync(Guid.NewGuid(), Guid.NewGuid(), day2);

        Assert.Equal("Q-001", day1First.QueueNumber);
        Assert.Equal("Q-002", day1Second.QueueNumber);
        Assert.Equal("Q-001", day2First.QueueNumber);

        var day1Numbers = await dbContext.QueueEntries
            .Where(e => e.QueueDate == new DateOnly(2026, 8, 29))
            .Select(e => e.QueueNumber)
            .ToListAsync();
        Assert.Equal(["Q-001", "Q-002"], day1Numbers.OrderBy(n => n));
    }

    [Fact]
    public async Task CreateQueueEntry_ForAUtcInstantAfterClinicMidnight_IsAssignedToTheNextLocalDate()
    {
        using var connection = OpenConnection();
        var options = OptionsFor(connection);
        await using var dbContext = new QueueDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        // 2026-08-29T20:00:00Z is 2026-08-30 01:30 in Asia/Colombo (UTC+5:30) - a UTC-date
        // reset would have put this on 2026-08-29 instead, 05:30 too early for the clinic.
        var lateUtcCheckIn = new DateTime(2026, 8, 29, 20, 0, 0, DateTimeKind.Utc);

        var service = CreateService(dbContext);
        await service.CreateQueueEntryAsync(Guid.NewGuid(), Guid.NewGuid(), lateUtcCheckIn);

        var entry = await dbContext.QueueEntries.SingleAsync();
        Assert.Equal(new DateOnly(2026, 8, 30), entry.QueueDate);
    }

    [Fact]
    public async Task CreateQueueEntry_WithASecondDeliveryOfTheSameEventId_CreatesNoSecondEntry()
    {
        using var connection = OpenConnection();
        var options = OptionsFor(connection);
        await using var dbContext = new QueueDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var service = CreateService(dbContext);
        var eventId = Guid.NewGuid();
        var patientId = Guid.NewGuid();

        var first = await service.CreateQueueEntryAsync(eventId, patientId, FixedCheckedInAtUtc);
        var redelivered = await service.CreateQueueEntryAsync(eventId, patientId, FixedCheckedInAtUtc);

        Assert.Equal(QueueEntryCreationOutcome.Created, first.Outcome);
        Assert.Equal("Q-001", first.QueueNumber);
        Assert.Equal(QueueEntryCreationOutcome.DuplicateEvent, redelivered.Outcome);
        Assert.Null(redelivered.QueueNumber);

        Assert.Equal(1, await dbContext.QueueEntries.CountAsync());
    }

    [Fact]
    public async Task CreateQueueEntry_WithADistinctEventIdForAPatientAlreadyQueuedToday_CreatesNoSecondEntry()
    {
        using var connection = OpenConnection();
        var options = OptionsFor(connection);
        await using var dbContext = new QueueDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var service = CreateService(dbContext);
        var patientId = Guid.NewGuid();

        var first = await service.CreateQueueEntryAsync(Guid.NewGuid(), patientId, FixedCheckedInAtUtc);
        var second = await service.CreateQueueEntryAsync(Guid.NewGuid(), patientId, FixedCheckedInAtUtc);

        Assert.Equal(QueueEntryCreationOutcome.Created, first.Outcome);
        Assert.Equal(QueueEntryCreationOutcome.AlreadyQueuedToday, second.Outcome);

        Assert.Equal(1, await dbContext.QueueEntries.CountAsync());
        // Both eventIds are recorded so a redelivery of either short-circuits on the eventId
        // check rather than re-running the AlreadyQueuedToday query on every replay.
        Assert.Equal(2, await dbContext.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task CreateQueueEntry_WhenAPriorCheckInIsSkippedAsAlreadyQueued_DoesNotBurnAQueueNumber()
    {
        using var connection = OpenConnection();
        var options = OptionsFor(connection);
        await using var dbContext = new QueueDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var service = CreateService(dbContext);
        var patientA = Guid.NewGuid();

        var firstForA = await service.CreateQueueEntryAsync(Guid.NewGuid(), patientA, FixedCheckedInAtUtc);
        var duplicateForA = await service.CreateQueueEntryAsync(Guid.NewGuid(), patientA, FixedCheckedInAtUtc);
        var firstForB = await service.CreateQueueEntryAsync(Guid.NewGuid(), Guid.NewGuid(), FixedCheckedInAtUtc);

        Assert.Equal("Q-001", firstForA.QueueNumber);
        Assert.Equal(QueueEntryCreationOutcome.AlreadyQueuedToday, duplicateForA.Outcome);
        // Not Q-003: the skipped duplicate for patient A must not have consumed a number.
        Assert.Equal("Q-002", firstForB.QueueNumber);
    }

    [Theory]
    [InlineData(9, "Q-010")]
    [InlineData(99, "Q-100")]
    public async Task CreateQueueEntry_PadsAndGrowsPastThreeDigitsWithoutTruncation(int seedLastNumber, string expectedNumber)
    {
        using var connection = OpenConnection();
        var options = OptionsFor(connection);
        await using var dbContext = new QueueDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var queueDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(FixedCheckedInAtUtc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")));
        dbContext.DailyQueueCounters.Add(new DailyQueueCounter { QueueDate = queueDate, LastNumber = seedLastNumber });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var result = await service.CreateQueueEntryAsync(Guid.NewGuid(), Guid.NewGuid(), FixedCheckedInAtUtc);

        Assert.Equal(expectedNumber, result.QueueNumber);
    }

    [Fact]
    public async Task QueueEntries_RejectsASecondInsertForTheSamePatientAndQueueDate()
    {
        using var connection = OpenConnection();
        var options = OptionsFor(connection);
        await using var dbContext = new QueueDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var queueDate = new DateOnly(2026, 8, 29);
        var patientId = Guid.NewGuid();

        dbContext.QueueEntries.Add(NewEntry(patientId, queueDate, "Q-001"));
        await dbContext.SaveChangesAsync();

        dbContext.QueueEntries.Add(NewEntry(patientId, queueDate, "Q-002"));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task QueueEntries_RejectsASecondInsertForTheSameQueueDateAndQueueNumber()
    {
        using var connection = OpenConnection();
        var options = OptionsFor(connection);
        await using var dbContext = new QueueDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var queueDate = new DateOnly(2026, 8, 29);

        dbContext.QueueEntries.Add(NewEntry(Guid.NewGuid(), queueDate, "Q-001"));
        await dbContext.SaveChangesAsync();

        dbContext.QueueEntries.Add(NewEntry(Guid.NewGuid(), queueDate, "Q-001"));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    private static QueueEntry NewEntry(Guid patientId, DateOnly queueDate, string queueNumber) => new()
    {
        PatientId = patientId,
        QueueDate = queueDate,
        QueueNumber = queueNumber,
        Status = QueueStatus.Waiting,
        RoomNumber = null
    };
}
