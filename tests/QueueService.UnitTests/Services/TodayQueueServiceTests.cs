using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QueueService.Data;
using QueueService.Models.Configuration;
using QueueService.Models.Entities;
using QueueService.Models.Enums;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

public class TodayQueueServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 2, 6, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static TodayQueueService CreateService(
        QueueDbContext dbContext,
        DateTimeOffset? utcNow = null) => new(
            dbContext,
            Options.Create(new QueueOptions { ClinicTimeZone = "Asia/Colombo" }),
            new FixedTimeProvider(utcNow ?? FixedUtcNow));

    [Fact]
    public async Task GetTodayReturnsEntriesInNumericQueueNumberOrder()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var today = new DateOnly(2026, 9, 2);
        dbContext.QueueEntries.AddRange(
            NewEntry(today, "Q-1000"),
            NewEntry(today, "Q-999"),
            NewEntry(today, "Q-002"));
        await dbContext.SaveChangesAsync();

        var entries = await CreateService(dbContext).GetTodayAsync();

        Assert.Equal(["Q-002", "Q-999", "Q-1000"], entries.Select(entry => entry.QueueNumber));
    }

    [Fact]
    public async Task GetTodayWhenNoPatientsAreQueuedReturnsEmptyCollection()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);

        var entries = await CreateService(dbContext).GetTodayAsync();

        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetTodayReturnsAllSupportedStatusValuesAndDisplayFields()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var today = new DateOnly(2026, 9, 2);
        var waiting = NewEntry(today, "Q-001", QueueStatus.Waiting);
        var inConsultation = NewEntry(today, "Q-002", QueueStatus.InConsultation);
        inConsultation.RoomNumber = "2";
        inConsultation.DoctorName = "Dr Nimal Silva";
        var completed = NewEntry(today, "Q-003", QueueStatus.Completed);
        completed.RoomNumber = "1";
        completed.DoctorName = "Dr Ayesha Perera";
        dbContext.QueueEntries.AddRange(waiting, inConsultation, completed);
        await dbContext.SaveChangesAsync();

        var entries = await CreateService(dbContext).GetTodayAsync();

        Assert.Collection(
            entries,
            entry =>
            {
                Assert.Equal(waiting.Id, entry.QueueId);
                Assert.Equal(waiting.PatientId, entry.PatientId);
                Assert.Equal(waiting.CheckedInAt, entry.CheckedInAt);
                Assert.Equal("WAITING", entry.Status);
                Assert.Null(entry.RoomNumber);
                Assert.Null(entry.DoctorName);
            },
            entry =>
            {
                Assert.Equal("IN_CONSULTATION", entry.Status);
                Assert.Equal("2", entry.RoomNumber);
                Assert.Equal("Dr Nimal Silva", entry.DoctorName);
            },
            entry =>
            {
                Assert.Equal("COMPLETED", entry.Status);
                Assert.Equal("1", entry.RoomNumber);
                Assert.Equal("Dr Ayesha Perera", entry.DoctorName);
            });
    }

    [Fact]
    public async Task GetTodayExcludesEntriesFromPreviousClinicDay()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var currentDayEntry = NewEntry(new DateOnly(2026, 9, 2), "Q-001");
        dbContext.QueueEntries.AddRange(
            NewEntry(new DateOnly(2026, 9, 1), "Q-001"),
            currentDayEntry);
        await dbContext.SaveChangesAsync();

        var entries = await CreateService(dbContext).GetTodayAsync();

        var entry = Assert.Single(entries);
        Assert.Equal(currentDayEntry.Id, entry.QueueId);
        Assert.Equal("Q-001", entry.QueueNumber);
    }

    [Fact]
    public async Task GetTodaySwitchesDaysAtClinicLocalMidnight()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var beforeMidnightEntry = NewEntry(new DateOnly(2026, 9, 1), "Q-004");
        var afterMidnightEntry = NewEntry(new DateOnly(2026, 9, 2), "Q-001");
        dbContext.QueueEntries.AddRange(beforeMidnightEntry, afterMidnightEntry);
        await dbContext.SaveChangesAsync();

        // Asia/Colombo is UTC+05:30: these UTC instants straddle midnight by one minute.
        var beforeMidnight = await CreateService(
            dbContext,
            new DateTimeOffset(2026, 9, 1, 18, 29, 0, TimeSpan.Zero)).GetTodayAsync();
        var afterMidnight = await CreateService(
            dbContext,
            new DateTimeOffset(2026, 9, 1, 18, 30, 0, TimeSpan.Zero)).GetTodayAsync();

        Assert.Equal(beforeMidnightEntry.Id, Assert.Single(beforeMidnight).QueueId);
        Assert.Equal(afterMidnightEntry.Id, Assert.Single(afterMidnight).QueueId);
    }

    [Fact]
    public async Task GetWaitingReturnsOnlyWaitingEntriesWithNullAssignmentFields()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var today = new DateOnly(2026, 9, 2);
        var waiting = NewEntry(today, "Q-001", QueueStatus.Waiting);
        var inConsultation = NewEntry(today, "Q-002", QueueStatus.InConsultation);
        inConsultation.RoomNumber = "2";
        inConsultation.DoctorName = "Dr Nimal Silva";
        dbContext.QueueEntries.AddRange(
            waiting,
            inConsultation,
            NewEntry(today, "Q-003", QueueStatus.Completed));
        await dbContext.SaveChangesAsync();

        var entries = await CreateService(dbContext).GetWaitingAsync();

        var entry = Assert.Single(entries);
        Assert.Equal(waiting.Id, entry.QueueId);
        Assert.Equal("WAITING", entry.Status);
        Assert.Null(entry.RoomNumber);
        Assert.Null(entry.DoctorName);
    }

    [Fact]
    public async Task GetWaitingReturnsEntriesInNumericQueueNumberOrder()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var today = new DateOnly(2026, 9, 2);
        dbContext.QueueEntries.AddRange(
            NewEntry(today, "Q-1000"),
            NewEntry(today, "Q-999"),
            NewEntry(today, "Q-002"));
        await dbContext.SaveChangesAsync();

        var entries = await CreateService(dbContext).GetWaitingAsync();

        Assert.Equal(["Q-002", "Q-999", "Q-1000"], entries.Select(entry => entry.QueueNumber));
    }

    [Fact]
    public async Task GetWaitingWhenNoPatientsAreWaitingReturnsEmptyCollection()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var today = new DateOnly(2026, 9, 2);
        dbContext.QueueEntries.AddRange(
            NewEntry(today, "Q-001", QueueStatus.InConsultation),
            NewEntry(today, "Q-002", QueueStatus.Completed));
        await dbContext.SaveChangesAsync();

        var entries = await CreateService(dbContext).GetWaitingAsync();

        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetWaitingAfterPatientEntersConsultationNoLongerReturnsPatient()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var entry = NewEntry(new DateOnly(2026, 9, 2), "Q-001");
        dbContext.QueueEntries.Add(entry);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        Assert.Single(await service.GetWaitingAsync());

        entry.Status = QueueStatus.InConsultation;
        entry.RoomNumber = "1";
        entry.DoctorName = "Dr Ayesha Perera";
        await dbContext.SaveChangesAsync();

        Assert.Empty(await service.GetWaitingAsync());
    }

    private static async Task<QueueDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var dbContext = new QueueDbContext(
            new DbContextOptionsBuilder<QueueDbContext>().UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }

    private static QueueEntry NewEntry(
        DateOnly queueDate,
        string queueNumber,
        QueueStatus status = QueueStatus.Waiting) => new()
        {
            PatientId = Guid.NewGuid(),
            QueueDate = queueDate,
            QueueNumber = queueNumber,
            Status = status,
            CheckedInAt = new DateTime(2026, 9, 2, 5, 30, 0, DateTimeKind.Utc)
        };
}
