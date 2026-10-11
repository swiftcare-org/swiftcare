using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueueService.Data;
using QueueService.Models.Entities;
using QueueService.Models.Enums;
using QueueService.Models.Events;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

// How the producer's completion time is stored for each DateTimeKind it can arrive with.
// Only a local time is converted; the other kinds are already UTC by the producers' contract.
// On a machine whose time zone is UTC, local and UTC times coincide (SWC-151 mutation testing).
public class QueueCompletionServiceEdgeCaseTests
{
    private static readonly DateTime CompletedAtUtc = new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LocalCompletionTimeIsConvertedToUtc()
    {
        var stored = await CompleteWithAsync(CompletedAtUtc.ToLocalTime());

        Assert.Equal(CompletedAtUtc, stored);
        Assert.Equal(DateTimeKind.Utc, stored.Kind);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task UtcOrUnspecifiedCompletionTimeIsStoredUnchanged(DateTimeKind kind)
    {
        var stored = await CompleteWithAsync(DateTime.SpecifyKind(CompletedAtUtc, kind));

        Assert.Equal(CompletedAtUtc, stored);
        Assert.Equal(DateTimeKind.Utc, stored.Kind);
    }

    private static async Task<DateTime> CompleteWithAsync(DateTime completedAt)
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new QueueDbContext(
            new DbContextOptionsBuilder<QueueDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var entry = new QueueEntry
        {
            PatientId = Guid.NewGuid(),
            DoctorId = Guid.NewGuid(),
            QueueDate = new DateOnly(2026, 9, 21),
            QueueNumber = "Q-001",
            Status = QueueStatus.InConsultation,
            CheckedInAt = new DateTime(2026, 9, 21, 5, 0, 0, DateTimeKind.Utc)
        };
        db.QueueEntries.Add(entry);
        await db.SaveChangesAsync();
        var completed = new ConsultationCompletedEvent(
            Guid.NewGuid(), Guid.NewGuid(), entry.Id, entry.PatientId, entry.DoctorId!.Value)
        {
            CompletedAt = completedAt
        };

        await new QueueCompletionService(db, TimeProvider.System).CompleteAsync(completed);

        return entry.CompletedAt!.Value;
    }
}
