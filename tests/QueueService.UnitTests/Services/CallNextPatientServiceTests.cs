using System.Runtime.CompilerServices;
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

public class CallNextPatientServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 8, 6, 30, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public async Task CallNextAssignsFirstWaitingPatientAndPublishesEvent()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var laterEntry = NewEntry("Q-010");
        var firstEntry = NewEntry("Q-002");
        dbContext.QueueEntries.AddRange(laterEntry, firstEntry);
        await dbContext.SaveChangesAsync();
        var publisher = SuccessfulPublisher(out var publishedEvent);
        var doctorId = Guid.NewGuid();

        var result = await CreateService(dbContext, publisher.Object).CallNextAsync(
            doctorId,
            " Dr. Amara Chen ",
            " R-204 ",
            "corr-call-next");

        Assert.Equal(CallNextPatientOutcome.Success, result.Outcome);
        var calledPatient = Assert.IsType<QueueService.Models.Dtos.CalledPatientResponse>(result.CalledPatient);
        Assert.Equal(firstEntry.Id, calledPatient.QueueId);
        Assert.Equal(firstEntry.PatientId, calledPatient.PatientId);
        Assert.Equal("Q-002", calledPatient.QueueNumber);
        Assert.Equal("IN_CONSULTATION", calledPatient.Status);
        Assert.Equal(doctorId, calledPatient.DoctorId);
        Assert.Equal("Dr. Amara Chen", calledPatient.DoctorName);
        Assert.Equal("R-204", calledPatient.RoomNumber);
        Assert.Equal(FixedUtcNow.UtcDateTime, calledPatient.CalledAt);

        var persisted = await dbContext.QueueEntries.SingleAsync(entry => entry.Id == firstEntry.Id);
        Assert.Equal(QueueStatus.InConsultation, persisted.Status);
        Assert.Equal(doctorId, persisted.DoctorId);
        Assert.Equal("Dr. Amara Chen", persisted.DoctorName);
        Assert.Equal("R-204", persisted.RoomNumber);
        Assert.Equal(FixedUtcNow.UtcDateTime, persisted.CalledAt);
        Assert.Equal(QueueStatus.Waiting, laterEntry.Status);

        Assert.NotNull(publishedEvent.Value);
        Assert.Equal(firstEntry.Id, publishedEvent.Value.QueueId);
        Assert.Equal(firstEntry.PatientId, publishedEvent.Value.PatientId);
        Assert.Equal("Q-002", publishedEvent.Value.QueueNumber);
        Assert.Equal(doctorId, publishedEvent.Value.DoctorId);
        Assert.Equal("Dr. Amara Chen", publishedEvent.Value.DoctorName);
        Assert.Equal("R-204", publishedEvent.Value.RoomNumber);
        Assert.Equal(FixedUtcNow.UtcDateTime, publishedEvent.Value.CalledAt);
        Assert.Equal("corr-call-next", publishedEvent.Value.CorrelationId);
    }

    [Fact]
    public async Task CallNextWhenDoctorAlreadyHasPatientReturnsOccupied()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var doctorId = Guid.NewGuid();
        var activeEntry = NewEntry("Q-001", QueueStatus.InConsultation);
        activeEntry.DoctorId = doctorId;
        activeEntry.DoctorName = "Dr. Amara Chen";
        activeEntry.RoomNumber = "R-204";
        activeEntry.CalledAt = FixedUtcNow.UtcDateTime.AddMinutes(-5);
        var waitingEntry = NewEntry("Q-002");
        dbContext.QueueEntries.AddRange(activeEntry, waitingEntry);
        await dbContext.SaveChangesAsync();
        var publisher = new Mock<IQueueEventPublisher>(MockBehavior.Strict);

        var result = await CreateService(dbContext, publisher.Object).CallNextAsync(
            doctorId,
            "Dr. Amara Chen",
            "R-205",
            "corr-occupied");

        Assert.Equal(CallNextPatientOutcome.DoctorOrRoomOccupied, result.Outcome);
        Assert.Null(result.CalledPatient);
        Assert.Equal(QueueStatus.Waiting, waitingEntry.Status);
    }

    [Fact]
    public async Task CallNextWhenRoomAlreadyHasPatientReturnsOccupied()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var activeEntry = NewEntry("Q-001", QueueStatus.InConsultation);
        activeEntry.DoctorId = Guid.NewGuid();
        activeEntry.DoctorName = "Dr. Existing";
        activeEntry.RoomNumber = "R-204";
        activeEntry.CalledAt = FixedUtcNow.UtcDateTime.AddMinutes(-5);
        var waitingEntry = NewEntry("Q-002");
        dbContext.QueueEntries.AddRange(activeEntry, waitingEntry);
        await dbContext.SaveChangesAsync();
        var publisher = new Mock<IQueueEventPublisher>(MockBehavior.Strict);

        var result = await CreateService(dbContext, publisher.Object).CallNextAsync(
            Guid.NewGuid(),
            "Dr. Amara Chen",
            "R-204",
            "corr-occupied");

        Assert.Equal(CallNextPatientOutcome.DoctorOrRoomOccupied, result.Outcome);
        Assert.Null(result.CalledPatient);
        Assert.Equal(QueueStatus.Waiting, waitingEntry.Status);
    }

    [Fact]
    public async Task CallNextWhenPoolIsEmptyReturnsNoPatientsWaiting()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var publisher = new Mock<IQueueEventPublisher>(MockBehavior.Strict);

        var result = await CreateService(dbContext, publisher.Object).CallNextAsync(
            Guid.NewGuid(),
            "Dr. Amara Chen",
            "R-204",
            "corr-empty");

        Assert.Equal(CallNextPatientOutcome.NoPatientsWaiting, result.Outcome);
        Assert.Null(result.CalledPatient);
    }

    [Fact]
    public async Task CalledPatientIsRemovedFromSharedWaitingPool()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.QueueEntries.Add(NewEntry("Q-001"));
        await dbContext.SaveChangesAsync();
        var publisher = SuccessfulPublisher(out _);

        await CreateService(dbContext, publisher.Object).CallNextAsync(
            Guid.NewGuid(),
            "Dr. Amara Chen",
            "R-204",
            "corr-removal");

        var waitingPool = await new TodayQueueService(
            dbContext,
            Options.Create(new QueueOptions { ClinicTimeZone = "Asia/Colombo" }),
            new FixedTimeProvider(FixedUtcNow)).GetWaitingAsync();
        Assert.Empty(waitingPool);
    }

    [Fact]
    public async Task EventPublishFailureRollsBackQueueAssignment()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var waitingEntry = NewEntry("Q-001");
        dbContext.QueueEntries.Add(waitingEntry);
        await dbContext.SaveChangesAsync();
        var publisher = new Mock<IQueueEventPublisher>();
        publisher
            .Setup(service => service.PublishPatientCalledAsync(
                It.IsAny<PatientCalledEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateService(dbContext, publisher.Object).CallNextAsync(
            Guid.NewGuid(),
            "Dr. Amara Chen",
            "R-204",
            "corr-failure");

        Assert.Equal(CallNextPatientOutcome.EventPublishFailed, result.Outcome);
        var persisted = await dbContext.QueueEntries
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == waitingEntry.Id);
        Assert.Equal(QueueStatus.Waiting, persisted.Status);
        Assert.Null(persisted.DoctorId);
        Assert.Null(persisted.DoctorName);
        Assert.Null(persisted.RoomNumber);
        Assert.Null(persisted.CalledAt);
    }

    private static CallNextPatientService CreateService(
        QueueDbContext dbContext,
        IQueueEventPublisher publisher) => new(
            dbContext,
            publisher,
            Options.Create(new QueueOptions { ClinicTimeZone = "Asia/Colombo" }),
            new FixedTimeProvider(FixedUtcNow),
            NullLogger<CallNextPatientService>.Instance);

    private static Mock<IQueueEventPublisher> SuccessfulPublisher(
        out StrongBox<PatientCalledEvent?> publishedEvent)
    {
        var eventBox = new StrongBox<PatientCalledEvent?>();
        var publisher = new Mock<IQueueEventPublisher>();
        publisher
            .Setup(service => service.PublishPatientCalledAsync(
                It.IsAny<PatientCalledEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<PatientCalledEvent, CancellationToken>((message, _) => eventBox.Value = message)
            .ReturnsAsync(true);
        publishedEvent = eventBox;
        return publisher;
    }

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

    private static QueueEntry NewEntry(
        string queueNumber,
        QueueStatus status = QueueStatus.Waiting) => new()
        {
            PatientId = Guid.NewGuid(),
            QueueDate = new DateOnly(2026, 9, 8),
            QueueNumber = queueNumber,
            Status = status,
            CheckedInAt = new DateTime(2026, 9, 8, 6, 0, 0, DateTimeKind.Utc)
        };
}
