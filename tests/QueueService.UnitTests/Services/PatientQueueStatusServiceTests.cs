using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QueueService.Data;
using QueueService.Models.Configuration;
using QueueService.Models.Entities;
using QueueService.Models.Enums;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

public class PatientQueueStatusServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 9, 2, 6, 0, 0, TimeSpan.Zero);

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

    private static PatientQueueStatusService CreateService(QueueDbContext dbContext) => new(
        dbContext,
        Options.Create(new QueueOptions { ClinicTimeZone = "Asia/Colombo" }),
        new FixedTimeProvider(FixedUtcNow));

    [Fact]
    public async Task GetTodayStatusForQueuedPatientReturnsQueueNumber()
    {
        using var connection = OpenConnection();
        await using var dbContext = new QueueDbContext(
            new DbContextOptionsBuilder<QueueDbContext>().UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();
        var patientId = Guid.NewGuid();
        dbContext.QueueEntries.Add(new QueueEntry
        {
            PatientId = patientId,
            QueueDate = new DateOnly(2026, 9, 2),
            QueueNumber = "Q-003",
            Status = QueueStatus.Waiting
        });
        await dbContext.SaveChangesAsync();

        var status = await CreateService(dbContext).GetTodayStatusAsync(patientId);

        Assert.True(status.IsCheckedIn);
        Assert.Equal("Q-003", status.QueueNumber);
    }

    [Fact]
    public async Task GetTodayStatusForPatientNotQueuedTodayReturnsNotCheckedIn()
    {
        using var connection = OpenConnection();
        await using var dbContext = new QueueDbContext(
            new DbContextOptionsBuilder<QueueDbContext>().UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();

        var status = await CreateService(dbContext).GetTodayStatusAsync(Guid.NewGuid());

        Assert.False(status.IsCheckedIn);
        Assert.Null(status.QueueNumber);
    }

    [Fact]
    public async Task GetTodayStatusIgnoresEntryFromPreviousClinicDay()
    {
        using var connection = OpenConnection();
        await using var dbContext = new QueueDbContext(
            new DbContextOptionsBuilder<QueueDbContext>().UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();
        var patientId = Guid.NewGuid();
        dbContext.QueueEntries.Add(new QueueEntry
        {
            PatientId = patientId,
            QueueDate = new DateOnly(2026, 9, 1),
            QueueNumber = "Q-010",
            Status = QueueStatus.Waiting
        });
        await dbContext.SaveChangesAsync();

        var status = await CreateService(dbContext).GetTodayStatusAsync(patientId);

        Assert.False(status.IsCheckedIn);
        Assert.Null(status.QueueNumber);
    }
}
