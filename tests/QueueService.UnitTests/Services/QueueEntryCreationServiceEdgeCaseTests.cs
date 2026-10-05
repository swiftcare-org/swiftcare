using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueueService.Data;
using QueueService.Models.Configuration;
using QueueService.Models.Entities;
using QueueService.Models.Enums;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

// Concurrency retries, the concurrent first-counter race and redelivery bookkeeping of
// QueueEntryCreationService. A save interceptor stands in for a competing consumer
// instance (SWC-151 mutation testing).
public class QueueEntryCreationServiceEdgeCaseTests
{
    private static readonly DateTime CheckedInAtUtc = new(2026, 8, 29, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly ClinicDate = new(2026, 8, 29);

    [Fact]
    public async Task RetriesAfterConcurrencyConflictsAndSucceedsOnTheLastAllowedAttempt()
    {
        var interceptor = new SaveInterceptor((_, attempt) => attempt <= 2
            ? throw new DbUpdateConcurrencyException("Another consumer updated the counter.")
            : Task.CompletedTask);
        using var connection = OpenConnection();
        await SeedCounterAsync(connection, lastNumber: 4);
        await using var dbContext = CreateDbContext(connection, interceptor);

        var result = await CreateService(dbContext)
            .CreateQueueEntryAsync(Guid.NewGuid(), Guid.NewGuid(), CheckedInAtUtc);

        Assert.Equal(QueueEntryCreationOutcome.Created, result.Outcome);
        Assert.Equal("Q-005", result.QueueNumber);
        Assert.Equal(3, interceptor.Calls);
        await using var verify = CreateDbContext(connection);
        Assert.Equal(5, (await verify.DailyQueueCounters.SingleAsync()).LastNumber);
        Assert.Equal(1, await verify.QueueEntries.CountAsync());
    }

    [Fact]
    public async Task StopsAfterTheConfiguredNumberOfAttemptsWithoutQueueingThePatient()
    {
        // Only the attempt count and the absence of an entry are asserted. The outcome the
        // service reports here is the subject of the bug raised from this analysis.
        var interceptor = new SaveInterceptor((_, _) =>
            throw new DbUpdateConcurrencyException("Another consumer updated the counter."));
        using var connection = OpenConnection();
        await SeedCounterAsync(connection, lastNumber: 4);
        await using var dbContext = CreateDbContext(connection, interceptor);

        await Record.ExceptionAsync(() => CreateService(dbContext)
            .CreateQueueEntryAsync(Guid.NewGuid(), Guid.NewGuid(), CheckedInAtUtc));

        Assert.Equal(3, interceptor.Calls);
        await using var verify = CreateDbContext(connection);
        Assert.Equal(0, await verify.QueueEntries.CountAsync());
    }

    [Fact]
    public async Task CounterCreatedConcurrentlyForTheFirstCheckInOfTheDayIsRetriedNotSkipped()
    {
        var interceptor = new SaveInterceptor(async (context, attempt) =>
        {
            if (attempt == 1)
            {
                // Another consumer inserts today's counter row just before this one does.
                await context.Database.ExecuteSqlAsync(
                    $"INSERT INTO DailyQueueCounters (QueueDate, LastNumber) VALUES ({ClinicDate}, {7})");
            }
        });
        using var connection = OpenConnection();
        await EnsureCreatedAsync(connection);
        await using var dbContext = CreateDbContext(connection, interceptor);

        var result = await CreateService(dbContext)
            .CreateQueueEntryAsync(Guid.NewGuid(), Guid.NewGuid(), CheckedInAtUtc);

        // The failed attempt's transaction is rolled back with the competing row in it, so the
        // retry starts the day's counter again.
        Assert.Equal(QueueEntryCreationOutcome.Created, result.Outcome);
        Assert.Equal("Q-001", result.QueueNumber);
        await using var verify = CreateDbContext(connection);
        Assert.Equal(1, await verify.QueueEntries.CountAsync());
    }

    [Fact]
    public async Task SkippedCheckInRecordsItsEventSoARedeliveryIsRecognized()
    {
        using var connection = OpenConnection();
        await EnsureCreatedAsync(connection);
        await using var dbContext = CreateDbContext(connection);
        var service = CreateService(dbContext);
        var patientId = Guid.NewGuid();
        var secondEventId = Guid.NewGuid();

        await service.CreateQueueEntryAsync(Guid.NewGuid(), patientId, CheckedInAtUtc);
        var skipped = await service.CreateQueueEntryAsync(secondEventId, patientId, CheckedInAtUtc);
        var redelivered = await service.CreateQueueEntryAsync(secondEventId, patientId, CheckedInAtUtc);

        Assert.Equal(QueueEntryCreationOutcome.AlreadyQueuedToday, skipped.Outcome);
        Assert.True(await dbContext.ProcessedEvents.AnyAsync(item => item.EventId == secondEventId));
        Assert.Equal(QueueEntryCreationOutcome.DuplicateEvent, redelivered.Outcome);
    }

    private static QueueEntryCreationService CreateService(QueueDbContext dbContext) => new(
        dbContext,
        Options.Create(new QueueOptions { ClinicTimeZone = "Asia/Colombo", MaxAllocationAttempts = 3 }),
        NullLogger<QueueEntryCreationService>.Instance);

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static QueueDbContext CreateDbContext(SqliteConnection connection, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<QueueDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptors)
            .Options);

    private static async Task EnsureCreatedAsync(SqliteConnection connection)
    {
        await using var dbContext = CreateDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
    }

    private static async Task SeedCounterAsync(SqliteConnection connection, int lastNumber)
    {
        await using var dbContext = CreateDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.DailyQueueCounters.Add(new DailyQueueCounter { QueueDate = ClinicDate, LastNumber = lastNumber });
        await dbContext.SaveChangesAsync();
    }

    // Counts the service's saves and runs an action before each one.
    private sealed class SaveInterceptor(Func<DbContext, int, Task> beforeSave) : SaveChangesInterceptor
    {
        public int Calls { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            await beforeSave(eventData.Context!, Calls);
            return result;
        }
    }
}
