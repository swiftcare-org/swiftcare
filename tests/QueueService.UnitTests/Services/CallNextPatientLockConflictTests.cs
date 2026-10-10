using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MySqlConnector;
using QueueService.Data;
using QueueService.Models.Configuration;
using QueueService.Models.Entities;
using QueueService.Models.Enums;
using QueueService.Models.Events;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

// SWC-128: when two doctors call the next patient at the same moment, MySQL rolls one call
// back as a deadlock. That call is retried instead of failing with a 500.
public class CallNextPatientLockConflictTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 9, 8, 6, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 8);
    private static readonly Guid DoctorId = Guid.NewGuid();

    [Theory]
    [InlineData(MySqlErrorCode.LockDeadlock)]
    [InlineData(MySqlErrorCode.LockWaitTimeout)]
    public async Task CallRolledBackByALockConflictIsRetriedAndSucceeds(MySqlErrorCode errorCode)
    {
        using var connection = OpenConnection();
        var conflicts = new LockConflictInterceptor(errorCode, failures: 1);
        await using var dbContext = await CreateDbContextAsync(connection, conflicts);
        var first = NewEntry("Q-001");
        dbContext.QueueEntries.AddRange(first, NewEntry("Q-002"));
        await dbContext.SaveChangesAsync();
        conflicts.Arm();
        var publisher = SuccessfulPublisher();

        var result = await CreateService(dbContext, publisher).CallNextAsync(DoctorId, "Dr. Amara Chen", "R-204", "corr");

        Assert.Equal(CallNextPatientOutcome.Success, result.Outcome);
        Assert.Equal("Q-001", result.CalledPatient!.QueueNumber);
        Assert.Equal(2, conflicts.SaveAttempts);
        publisher.Verify(
            item => item.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
        var stored = await ReadEntriesAsync(connection);
        var called = Assert.Single(stored, entry => entry.Status == QueueStatus.InConsultation);
        Assert.Equal(first.Id, called.Id);
        Assert.Equal(DoctorId, called.DoctorId);
        Assert.Equal("R-204", called.RoomNumber);
        Assert.Equal(FixedUtcNow.UtcDateTime, called.CalledAt);
    }

    [Fact]
    public async Task CallThatKeepsCollidingGivesUpWithoutChangingTheQueue()
    {
        using var connection = OpenConnection();
        var conflicts = new LockConflictInterceptor(MySqlErrorCode.LockDeadlock, failures: int.MaxValue);
        await using var dbContext = await CreateDbContextAsync(connection, conflicts);
        dbContext.QueueEntries.Add(NewEntry("Q-001"));
        await dbContext.SaveChangesAsync();
        conflicts.Arm();
        var publisher = new Mock<IQueueEventPublisher>(MockBehavior.Strict);

        var result = await CreateService(dbContext, publisher).CallNextAsync(DoctorId, "Dr. Amara Chen", "R-204", "corr");

        Assert.Equal(CallNextPatientOutcome.ConcurrentCallConflict, result.Outcome);
        Assert.Null(result.CalledPatient);
        Assert.Equal(3, conflicts.SaveAttempts);
        publisher.VerifyNoOtherCalls();
        var stored = Assert.Single(await ReadEntriesAsync(connection));
        Assert.Equal(QueueStatus.Waiting, stored.Status);
        Assert.Null(stored.DoctorId);
        Assert.Null(stored.RoomNumber);
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    // Zero or fewer still makes one attempt.
    [InlineData(0, 1)]
    [InlineData(-2, 1)]
    public async Task NumberOfAttemptsComesFromConfiguration(int configuredAttempts, int expectedAttempts)
    {
        using var connection = OpenConnection();
        var conflicts = new LockConflictInterceptor(MySqlErrorCode.LockDeadlock, failures: int.MaxValue);
        await using var dbContext = await CreateDbContextAsync(connection, conflicts);
        dbContext.QueueEntries.Add(NewEntry("Q-001"));
        await dbContext.SaveChangesAsync();
        conflicts.Arm();

        var result = await CreateService(dbContext, SuccessfulPublisher(), configuredAttempts)
            .CallNextAsync(DoctorId, "Dr. Amara Chen", "R-204", "corr");

        Assert.Equal(CallNextPatientOutcome.ConcurrentCallConflict, result.Outcome);
        Assert.Equal(expectedAttempts, conflicts.SaveAttempts);
    }

    [Fact]
    public void ThreeAttemptsAreMadeByDefault()
    {
        Assert.Equal(3, new QueueOptions { ClinicTimeZone = "Asia/Colombo" }.MaxCallNextAttempts);
    }

    // Only lock conflicts are retried. Any other database failure is a real fault.
    [Fact]
    public async Task OtherDatabaseFailureIsNotRetried()
    {
        using var connection = OpenConnection();
        var conflicts = new LockConflictInterceptor(MySqlErrorCode.DuplicateKeyEntry, failures: int.MaxValue);
        await using var dbContext = await CreateDbContextAsync(connection, conflicts);
        dbContext.QueueEntries.Add(NewEntry("Q-001"));
        await dbContext.SaveChangesAsync();
        conflicts.Arm();
        var publisher = new Mock<IQueueEventPublisher>(MockBehavior.Strict);

        await Assert.ThrowsAsync<MySqlException>(() =>
            CreateService(dbContext, publisher).CallNextAsync(DoctorId, "Dr. Amara Chen", "R-204", "corr"));

        Assert.Equal(1, conflicts.SaveAttempts);
        publisher.VerifyNoOtherCalls();
    }

    // A retry reads the queue again, so an entry another doctor called in the meantime is
    // not called twice: the retry takes the next waiting patient.
    [Fact]
    public async Task RetryCallsTheNextPatientWhenAnotherDoctorTookTheFirst()
    {
        using var connection = OpenConnection();
        var conflicts = new LockConflictInterceptor(MySqlErrorCode.LockDeadlock, failures: 1);
        var first = NewEntry("Q-001");
        var second = NewEntry("Q-002");
        // Mirrors InnoDB: the other doctor's call commits once this one has been rolled back.
        var rival = new AfterRollbackInterceptor(() =>
        {
            using var otherDoctor = new QueueDbContext(
                new DbContextOptionsBuilder<QueueDbContext>().UseSqlite(connection).Options);
            var taken = otherDoctor.QueueEntries.Single(entry => entry.Id == first.Id);
            taken.Status = QueueStatus.InConsultation;
            taken.DoctorId = Guid.NewGuid();
            taken.DoctorName = "Dr. Silva";
            taken.RoomNumber = "R-101";
            otherDoctor.SaveChanges();
        });
        await using var dbContext = await CreateDbContextAsync(connection, conflicts, rival);
        dbContext.QueueEntries.AddRange(first, second);
        await dbContext.SaveChangesAsync();
        conflicts.Arm();

        var result = await CreateService(dbContext, SuccessfulPublisher())
            .CallNextAsync(DoctorId, "Dr. Amara Chen", "R-204", "corr");

        Assert.Equal(CallNextPatientOutcome.Success, result.Outcome);
        Assert.Equal(second.Id, result.CalledPatient!.QueueId);
        Assert.Equal("Q-002", result.CalledPatient.QueueNumber);
    }

    private static CallNextPatientService CreateService(
        QueueDbContext dbContext,
        Mock<IQueueEventPublisher> publisher,
        int maxAttempts = 3) => new(
            dbContext,
            publisher.Object,
            Options.Create(new QueueOptions { ClinicTimeZone = "Asia/Colombo", MaxCallNextAttempts = maxAttempts }),
            new FixedTimeProvider(),
            NullLogger<CallNextPatientService>.Instance);

    private static Mock<IQueueEventPublisher> SuccessfulPublisher()
    {
        var publisher = new Mock<IQueueEventPublisher>();
        publisher
            .Setup(item => item.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return publisher;
    }

    private static QueueEntry NewEntry(string queueNumber) => new()
    {
        PatientId = Guid.NewGuid(),
        QueueDate = Today,
        QueueNumber = queueNumber,
        Status = QueueStatus.Waiting,
        CheckedInAt = new DateTime(2026, 9, 8, 6, 0, 0, DateTimeKind.Utc)
    };

    // What is committed, read through a second context so nothing tracked can hide it.
    private static async Task<List<QueueEntry>> ReadEntriesAsync(SqliteConnection connection)
    {
        await using var reader = new QueueDbContext(
            new DbContextOptionsBuilder<QueueDbContext>().UseSqlite(connection).Options);
        return await reader.QueueEntries.AsNoTracking().ToListAsync();
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static async Task<QueueDbContext> CreateDbContextAsync(
        SqliteConnection connection,
        params IInterceptor[] interceptors)
    {
        var dbContext = new QueueDbContext(
            new DbContextOptionsBuilder<QueueDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(interceptors)
                .Options);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedUtcNow;
    }

    // Fails the first `failures` saves the way MySQL does when it picks this transaction as
    // a deadlock victim. MySqlException has no public constructor, so it is built the way
    // MySqlConnector builds it.
    private sealed class LockConflictInterceptor(MySqlErrorCode errorCode, int failures) : SaveChangesInterceptor
    {
        private bool _armed;
        private int _failed;

        public int SaveAttempts { get; private set; }

        public void Arm() => _armed = true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_armed)
            {
                return ValueTask.FromResult(result);
            }

            SaveAttempts++;
            if (_failed >= failures)
            {
                return ValueTask.FromResult(result);
            }

            _failed++;
            throw CreateMySqlException(errorCode);
        }

        private static MySqlException CreateMySqlException(MySqlErrorCode code) =>
            (MySqlException)Activator.CreateInstance(
                typeof(MySqlException),
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                args: [code, "Simulated lock conflict"],
                culture: null)!;
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
