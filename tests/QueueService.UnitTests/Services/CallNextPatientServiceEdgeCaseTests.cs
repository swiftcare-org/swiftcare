using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using QueueService.Data;
using QueueService.Models.Configuration;
using QueueService.Models.Entities;
using QueueService.Models.Enums;
using QueueService.Models.Events;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

// Guard clauses, queue-number ordering, other days' entries and the failed-publish path of
// CallNextPatientService (SWC-151 mutation testing).
public class CallNextPatientServiceEdgeCaseTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 9, 8, 6, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 8);

    [Fact]
    public async Task CallNextRejectsEmptyDoctorId()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService(dbContext, StrictPublisher()).CallNextAsync(Guid.Empty, "Dr. Amara Chen", "R-204", "corr"));

        Assert.Equal("doctorId", exception.ParamName);
        Assert.StartsWith("Doctor ID must be provided.", exception.Message);
    }

    [Theory]
    [InlineData("   ", "R-204", "corr", "doctorName")]
    [InlineData("Dr. Amara Chen", "   ", "corr", "roomNumber")]
    [InlineData("Dr. Amara Chen", "R-204", "   ", "correlationId")]
    public async Task CallNextRejectsBlankIdentityValuesWithoutCallingAnyone(
        string doctorName,
        string roomNumber,
        string correlationId,
        string parameterName)
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.QueueEntries.Add(NewEntry("Q-001"));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService(dbContext, StrictPublisher()).CallNextAsync(
                Guid.NewGuid(),
                doctorName,
                roomNumber,
                correlationId));

        Assert.Equal(parameterName, exception.ParamName);
        Assert.Equal(QueueStatus.Waiting, (await dbContext.QueueEntries.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task CallNextKeepsNumericOrderPastQ999()
    {
        // As text "Q-1000" sorts before "Q-999", so the shorter number must be called first.
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.QueueEntries.AddRange(NewEntry("Q-1000"), NewEntry("Q-999"));
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, SuccessfulPublisher())
            .CallNextAsync(Guid.NewGuid(), "Dr. Amara Chen", "R-204", "corr");

        Assert.Equal(CallNextPatientOutcome.Success, result.Outcome);
        Assert.Equal("Q-999", result.CalledPatient!.QueueNumber);
    }

    [Fact]
    public async Task DoctorWhoCompletedAConsultationTodayCanCallTheNextPatient()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var doctorId = Guid.NewGuid();
        var completed = NewEntry("Q-001", QueueStatus.Completed);
        completed.DoctorId = doctorId;
        completed.DoctorName = "Dr. Amara Chen";
        completed.RoomNumber = "R-204";
        completed.CalledAt = FixedUtcNow.UtcDateTime.AddMinutes(-30);
        dbContext.QueueEntries.AddRange(completed, NewEntry("Q-002"));
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, SuccessfulPublisher())
            .CallNextAsync(doctorId, "Dr. Amara Chen", "R-204", "corr");

        Assert.Equal(CallNextPatientOutcome.Success, result.Outcome);
        Assert.Equal("Q-002", result.CalledPatient!.QueueNumber);
    }

    [Fact]
    public async Task ConsultationLeftOpenOnAnEarlierDayStillBlocksTheDoctorToday()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var doctorId = Guid.NewGuid();
        var stale = NewEntry("Q-007", QueueStatus.InConsultation, Today.AddDays(-1));
        stale.DoctorId = doctorId;
        stale.DoctorName = "Dr. Amara Chen";
        stale.RoomNumber = "R-204";
        stale.CalledAt = FixedUtcNow.UtcDateTime.AddDays(-1);
        dbContext.QueueEntries.AddRange(stale, NewEntry("Q-001"));
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, SuccessfulPublisher())
            .CallNextAsync(doctorId, "Dr. Amara Chen", "R-204", "corr");

        Assert.Equal(CallNextPatientOutcome.DoctorOrRoomOccupied, result.Outcome);
        Assert.Null(result.CalledPatient);
    }

    [Fact]
    public async Task PatientStillWaitingFromAnEarlierDayIsNotCalled()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var yesterday = NewEntry("Q-001", QueueStatus.Waiting, Today.AddDays(-1));
        dbContext.QueueEntries.Add(yesterday);
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, StrictPublisher())
            .CallNextAsync(Guid.NewGuid(), "Dr. Amara Chen", "R-204", "corr");

        Assert.Equal(CallNextPatientOutcome.NoPatientsWaiting, result.Outcome);
        Assert.Equal(QueueStatus.Waiting, (await dbContext.QueueEntries.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task FailedPublishLeavesNoStaleAssignmentInTheContext()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var waiting = NewEntry("Q-001");
        dbContext.QueueEntries.Add(waiting);
        await dbContext.SaveChangesAsync();
        var publisher = new Mock<IQueueEventPublisher>();
        publisher
            .Setup(service => service.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateService(dbContext, publisher).CallNextAsync(
            Guid.NewGuid(),
            "Dr. Amara Chen",
            "R-204",
            "corr");

        Assert.Equal(CallNextPatientOutcome.EventPublishFailed, result.Outcome);
        Assert.Empty(dbContext.ChangeTracker.Entries());
        var reloaded = await dbContext.QueueEntries.SingleAsync(entry => entry.Id == waiting.Id);
        Assert.Equal(QueueStatus.Waiting, reloaded.Status);
        Assert.Null(reloaded.DoctorId);
    }

    private static CallNextPatientService CreateService(
        QueueDbContext dbContext,
        Mock<IQueueEventPublisher> publisher) => new(
            dbContext,
            publisher.Object,
            Options.Create(new QueueOptions { ClinicTimeZone = "Asia/Colombo" }),
            new FixedTimeProvider(),
            NullLogger<CallNextPatientService>.Instance);

    private static Mock<IQueueEventPublisher> StrictPublisher() => new(MockBehavior.Strict);

    private static Mock<IQueueEventPublisher> SuccessfulPublisher()
    {
        var publisher = new Mock<IQueueEventPublisher>();
        publisher
            .Setup(service => service.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return publisher;
    }

    private static QueueEntry NewEntry(
        string queueNumber,
        QueueStatus status = QueueStatus.Waiting,
        DateOnly? queueDate = null) => new()
        {
            PatientId = Guid.NewGuid(),
            QueueDate = queueDate ?? Today,
            QueueNumber = queueNumber,
            Status = status,
            CheckedInAt = new DateTime(2026, 9, 8, 6, 0, 0, DateTimeKind.Utc)
        };

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
