using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QueueService.Data;
using QueueService.Models.Configuration;
using QueueService.Models.Entities;
using QueueService.Models.Enums;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

// Guard clauses, ordering and incomplete-data handling of TodayQueueService
// (SWC-151 mutation testing).
public class TodayQueueServiceEdgeCaseTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 9, 8, 6, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 8);

    [Fact]
    public async Task GetCurrentRejectsEmptyDoctorId()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService(dbContext).GetCurrentForDoctorAsync(Guid.Empty));

        Assert.Equal("doctorId", exception.ParamName);
        Assert.StartsWith("Doctor ID must be provided.", exception.Message);
    }

    [Fact]
    public async Task GetCurrentReturnsTheMostRecentlyCalledPatient()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var doctorId = Guid.NewGuid();
        dbContext.QueueEntries.AddRange(
            ActiveEntry("Q-001", doctorId, FixedUtcNow.UtcDateTime.AddMinutes(-20)),
            ActiveEntry("Q-002", doctorId, FixedUtcNow.UtcDateTime.AddMinutes(-5)));
        await dbContext.SaveChangesAsync();

        var current = await CreateService(dbContext).GetCurrentForDoctorAsync(doctorId);

        Assert.Equal("Q-002", current!.QueueNumber);
    }

    [Theory]
    [InlineData(nameof(QueueEntry.DoctorName), "The active queue entry has no doctor name.")]
    [InlineData(nameof(QueueEntry.RoomNumber), "The active queue entry has no room number.")]
    [InlineData(nameof(QueueEntry.CalledAt), "The active queue entry has no call time.")]
    public async Task GetCurrentFailsLoudlyWhenTheActiveEntryIsIncomplete(string missingField, string message)
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var doctorId = Guid.NewGuid();
        var entry = ActiveEntry("Q-001", doctorId, FixedUtcNow.UtcDateTime);
        typeof(QueueEntry).GetProperty(missingField)!.SetValue(entry, null);
        dbContext.QueueEntries.Add(entry);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(dbContext).GetCurrentForDoctorAsync(doctorId));

        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public async Task DisplayListsRoomsWithTheSameLengthAlphabetically()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.QueueEntries.AddRange(
            ActiveEntry("Q-002", Guid.NewGuid(), FixedUtcNow.UtcDateTime, "R-205"),
            ActiveEntry("Q-001", Guid.NewGuid(), FixedUtcNow.UtcDateTime, "R-204"));
        await dbContext.SaveChangesAsync();

        var display = await CreateService(dbContext).GetDisplayAsync();

        Assert.Equal(["R-204", "R-205"], display.CurrentRooms.Select(room => room.RoomNumber));
    }

    [Fact]
    public async Task TodayListRejectsAStatusItDoesNotKnow()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.QueueEntries.Add(new QueueEntry
        {
            PatientId = Guid.NewGuid(),
            QueueDate = Today,
            QueueNumber = "Q-001",
            Status = (QueueStatus)99,
            CheckedInAt = FixedUtcNow.UtcDateTime
        });
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CreateService(dbContext).GetTodayAsync());

        Assert.StartsWith("Unsupported queue status.", exception.Message);
    }

    private static QueueEntry ActiveEntry(
        string queueNumber,
        Guid doctorId,
        DateTime calledAt,
        string roomNumber = "R-204") => new()
        {
            PatientId = Guid.NewGuid(),
            QueueDate = Today,
            QueueNumber = queueNumber,
            Status = QueueStatus.InConsultation,
            CheckedInAt = FixedUtcNow.UtcDateTime.AddHours(-1),
            DoctorId = doctorId,
            DoctorName = "Dr. Amara Chen",
            RoomNumber = roomNumber,
            CalledAt = calledAt
        };

    private static TodayQueueService CreateService(QueueDbContext dbContext) => new(
        dbContext,
        Options.Create(new QueueOptions { ClinicTimeZone = "Asia/Colombo" }),
        new FixedTimeProvider());

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static async Task<QueueDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var dbContext = new QueueDbContext(
            new DbContextOptionsBuilder<QueueDbContext>().UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedUtcNow;
    }
}
