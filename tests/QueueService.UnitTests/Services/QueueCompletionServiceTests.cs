using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueueService.Data;
using QueueService.Models.Entities;
using QueueService.Models.Enums;
using QueueService.Models.Events;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

public class QueueCompletionServiceTests
{
    [Fact]
    public async Task CompletionUpdatesQueueAndRecordsEventExactlyOnce()
    {
        using var connection = OpenConnection();
        await using var db = await CreateDbContextAsync(connection);
        var entry = NewActiveEntry();
        db.QueueEntries.Add(entry);
        await db.SaveChangesAsync();
        var completedEvent = NewEvent(entry);
        var service = new QueueCompletionService(db, TimeProvider.System);

        var first = await service.CompleteAsync(completedEvent);
        var duplicate = await service.CompleteAsync(completedEvent);

        Assert.Equal(QueueCompletionOutcome.Completed, first);
        Assert.Equal(QueueCompletionOutcome.DuplicateEvent, duplicate);
        Assert.Equal(QueueStatus.Completed, (await db.QueueEntries.SingleAsync()).Status);
        Assert.Equal(completedEvent.EventId, (await db.ProcessedEvents.SingleAsync()).EventId);
    }

    [Fact]
    public async Task DifferentEventForAlreadyCompletedQueueDoesNotChangeEntryAgain()
    {
        using var connection = OpenConnection();
        await using var db = await CreateDbContextAsync(connection);
        var entry = NewActiveEntry();
        db.QueueEntries.Add(entry);
        await db.SaveChangesAsync();
        var service = new QueueCompletionService(db, TimeProvider.System);

        await service.CompleteAsync(NewEvent(entry));
        var outcome = await service.CompleteAsync(NewEvent(entry));

        Assert.Equal(QueueCompletionOutcome.AlreadyCompleted, outcome);
        Assert.Equal(QueueStatus.Completed, entry.Status);
        Assert.Equal(2, await db.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task MismatchedPatientDoesNotCompleteOrRecordEvent()
    {
        using var connection = OpenConnection();
        await using var db = await CreateDbContextAsync(connection);
        var entry = NewActiveEntry();
        db.QueueEntries.Add(entry);
        await db.SaveChangesAsync();
        var completedEvent = NewEvent(entry) with { PatientId = Guid.NewGuid() };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new QueueCompletionService(db, TimeProvider.System).CompleteAsync(completedEvent));

        Assert.Equal(QueueStatus.InConsultation, entry.Status);
        Assert.Empty(await db.ProcessedEvents.ToListAsync());
    }

    private static QueueEntry NewActiveEntry() => new()
    {
        PatientId = Guid.NewGuid(),
        DoctorId = Guid.NewGuid(),
        QueueDate = new DateOnly(2026, 9, 20),
        QueueNumber = "Q-001",
        Status = QueueStatus.InConsultation,
        CheckedInAt = new DateTime(2026, 9, 20, 5, 0, 0, DateTimeKind.Utc)
    };

    private static ConsultationCompletedEvent NewEvent(QueueEntry entry) =>
        new(Guid.NewGuid(), Guid.NewGuid(), entry.Id, entry.PatientId, entry.DoctorId!.Value);

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static async Task<QueueDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var db = new QueueDbContext(
            new DbContextOptionsBuilder<QueueDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
