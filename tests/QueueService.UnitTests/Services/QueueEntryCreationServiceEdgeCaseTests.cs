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
        // Issue #124: losing the counter race on every attempt is a failure the consumer
        // must retry. It used to be reported as "already queued", which lost the check-in.
        var interceptor = new SaveInterceptor((_, _) =>
            throw new DbUpdateConcurrencyException("Another consumer updated the counter."));
        using var connection = OpenConnection();
        await SeedCounterAsync(connection, lastNumber: 4);
        await using var dbContext = CreateDbContext(connection, interceptor);
        var eventId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(dbContext)
            .CreateQueueEntryAsync(eventId, Guid.NewGuid(), CheckedInAtUtc));

        Assert.Equal(
            $"Failed to allocate a queue number for {ClinicDate:yyyy-MM-dd} after 3 attempts due to concurrent contention.",
            exception.Message);
        Assert.Equal(3, interceptor.Calls);
        await using var verify = CreateDbContext(connection);
        Assert.Equal(0, await verify.QueueEntries.CountAsync());
        // Nothing marks the event as handled, so a redelivery is processed, not skipped.
        Assert.False(await verify.ProcessedEvents.AnyAsync(processed => processed.EventId == eventId));
        Assert.Equal(4, (await verify.DailyQueueCounters.SingleAsync()).LastNumber);
    }

    // The same event redelivered after the contention has passed is queued normally.
    [Fact]
    public async Task CheckInThatFailedToAllocateIsQueuedWhenItIsRedelivered()
    {
        var failing = true;
        var interceptor = new SaveInterceptor((_, _) => failing
            ? throw new DbUpdateConcurrencyException("Another consumer updated the counter.")
            : Task.CompletedTask);
        using var connection = OpenConnection();
        await SeedCounterAsync(connection, lastNumber: 4);
        await using var dbContext = CreateDbContext(connection, interceptor);
        var eventId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var service = CreateService(dbContext);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateQueueEntryAsync(eventId, patientId, CheckedInAtUtc));
        failing = false;

        var result = await service.CreateQueueEntryAsync(eventId, patientId, CheckedInAtUtc);

        Assert.Equal(QueueEntryCreationOutcome.Created, result.Outcome);
        Assert.Equal("Q-005", result.QueueNumber);
        await using var verify = CreateDbContext(connection);
        Assert.Equal(patientId, (await verify.QueueEntries.SingleAsync()).PatientId);
    }

    // Issue #124: the "already queued" answer is kept for what it was written for, a second
    // check-in for the same patient that the unique constraint rejects. It is not retried.
    [Fact]
    public async Task SecondCheckInRejectedBecauseAnotherConsumerQueuedThePatientIsReportedAsAlreadyQueued()
    {
        var patientId = Guid.NewGuid();
        using var connection = OpenConnection();
        await SeedCounterAsync(connection, lastNumber: 4);
        var interceptor = new SaveInterceptor((_, _) =>
            throw new DbUpdateException("Duplicate entry for the patient and date."));
        // Mirrors MySQL: the save fails because the other consumer's entry has just committed.
        var otherConsumer = new AfterRollbackInterceptor(() =>
        {
            using var other = CreateDbContext(connection);
            other.QueueEntries.Add(new QueueEntry
            {
                PatientId = patientId,
                QueueDate = ClinicDate,
                QueueNumber = "Q-005",
                Status = QueueStatus.Waiting,
                CheckedInAt = CheckedInAtUtc
            });
            other.SaveChanges();
        });
        await using var dbContext = CreateDbContext(connection, interceptor, otherConsumer);

        var result = await CreateService(dbContext)
            .CreateQueueEntryAsync(Guid.NewGuid(), patientId, CheckedInAtUtc);

        Assert.Equal(QueueEntryCreationOutcome.AlreadyQueuedToday, result.Outcome);
        Assert.Null(result.QueueNumber);
        Assert.Equal(1, interceptor.Calls);
        await using var verify = CreateDbContext(connection);
        Assert.Equal("Q-005", (await verify.QueueEntries.SingleAsync()).QueueNumber);
    }

    // A save can fail for reasons that have nothing to do with a duplicate: a deadlock, a
    // lock timeout, a lost connection. The patient is not queued, so the failure must reach
    // the consumer, which then leaves the message for redelivery.
    [Fact]
    public async Task SaveFailureWhenThePatientIsNotQueuedIsRethrownSoTheCheckInIsRetried()
    {
        var failure = new DbUpdateException("Deadlock found when trying to get lock.");
        var interceptor = new SaveInterceptor((_, _) => throw failure);
        using var connection = OpenConnection();
        await SeedCounterAsync(connection, lastNumber: 4);
        await using var dbContext = CreateDbContext(connection, interceptor);
        var eventId = Guid.NewGuid();

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => CreateService(dbContext)
            .CreateQueueEntryAsync(eventId, Guid.NewGuid(), CheckedInAtUtc));

        Assert.Same(failure, thrown);
        Assert.Equal(1, interceptor.Calls);
        Assert.Empty(dbContext.ChangeTracker.Entries());
        await using var verify = CreateDbContext(connection);
        Assert.Equal(0, await verify.QueueEntries.CountAsync());
        Assert.False(await verify.ProcessedEvents.AnyAsync(processed => processed.EventId == eventId));
        Assert.Equal(4, (await verify.DailyQueueCounters.SingleAsync()).LastNumber);
    }

    // Another patient queued today does not make this patient "already queued".
    [Fact]
    public async Task SaveFailureIsRethrownEvenWhenOtherPatientsAreQueuedToday()
    {
        using var connection = OpenConnection();
        await SeedCounterAsync(connection, lastNumber: 4);
        await using (var seed = CreateDbContext(connection))
        {
            seed.QueueEntries.Add(new QueueEntry
            {
                PatientId = Guid.NewGuid(),
                QueueDate = ClinicDate,
                QueueNumber = "Q-004",
                Status = QueueStatus.Waiting,
                CheckedInAt = CheckedInAtUtc
            });
            await seed.SaveChangesAsync();
        }

        var interceptor = new SaveInterceptor((_, _) => throw new DbUpdateException("Lock wait timeout exceeded."));
        await using var dbContext = CreateDbContext(connection, interceptor);

        await Assert.ThrowsAsync<DbUpdateException>(() => CreateService(dbContext)
            .CreateQueueEntryAsync(Guid.NewGuid(), Guid.NewGuid(), CheckedInAtUtc));
    }

    // The same patient queued on another day does not count either.
    [Fact]
    public async Task SaveFailureIsRethrownWhenThePatientWasOnlyQueuedOnAnotherDay()
    {
        var patientId = Guid.NewGuid();
        using var connection = OpenConnection();
        await SeedCounterAsync(connection, lastNumber: 4);
        await using (var seed = CreateDbContext(connection))
        {
            seed.QueueEntries.Add(new QueueEntry
            {
                PatientId = patientId,
                QueueDate = ClinicDate.AddDays(-1),
                QueueNumber = "Q-001",
                Status = QueueStatus.Completed,
                CheckedInAt = CheckedInAtUtc.AddDays(-1)
            });
            await seed.SaveChangesAsync();
        }

        var interceptor = new SaveInterceptor((_, _) => throw new DbUpdateException("Deadlock found."));
        await using var dbContext = CreateDbContext(connection, interceptor);

        await Assert.ThrowsAsync<DbUpdateException>(() => CreateService(dbContext)
            .CreateQueueEntryAsync(Guid.NewGuid(), patientId, CheckedInAtUtc));
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

    // Runs once, right after the first rollback, when no transaction is open any more.
    private sealed class AfterRollbackInterceptor(Action action) : DbTransactionInterceptor
    {
        private Action? _action = action;

        public override Task TransactionRolledBackAsync(
            System.Data.Common.DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            var pending = _action;
            _action = null;
            pending?.Invoke();
            return Task.CompletedTask;
        }
    }
}
