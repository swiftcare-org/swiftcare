using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NotificationService.Data;
using NotificationService.Models.Entities;
using NotificationService.Models.Enums;

namespace NotificationService.UnitTests.Services;

// An in-memory SQLite database with the real schema, so unique indexes are enforced the
// way MySQL enforces them.
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private TestDatabase(SqliteConnection connection, NotificationDbContext dbContext)
    {
        _connection = connection;
        DbContext = dbContext;
    }

    public NotificationDbContext DbContext { get; }

    // beforeSave runs just before the DbContext's own SaveChanges, to simulate what
    // another instance or the database does at that moment.
    public static async Task<TestDatabase> CreateAsync(Func<TestDatabase, Task>? beforeSave = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var interceptor = new BeforeSaveInterceptor();
        var dbContext = new NotificationDbContext(
            new DbContextOptionsBuilder<NotificationDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(interceptor)
                .Options);
        await dbContext.Database.EnsureCreatedAsync();

        var database = new TestDatabase(connection, dbContext);
        if (beforeSave is not null)
        {
            interceptor.BeforeSave = () => beforeSave(database);
        }

        return database;
    }

    // A second context on the same database: another request, or another instance.
    public NotificationDbContext NewContext() => new(
        new DbContextOptionsBuilder<NotificationDbContext>().UseSqlite(_connection).Options);

    public async Task SeedAsync(params Notification[] notifications)
    {
        await using var context = NewContext();
        context.Notifications.AddRange(notifications);
        await context.SaveChangesAsync();
    }

    public static Notification NewNotification(
        Guid? eventId = null,
        NotificationType type = NotificationType.PatientCheckedIn,
        DateTime? occurredAt = null,
        DateTime? receivedAt = null,
        Guid? id = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            EventId = eventId ?? Guid.NewGuid(),
            Type = type,
            PatientId = Guid.NewGuid(),
            IsNewPatient = type == NotificationType.PatientCheckedIn ? true : null,
            QueueNumber = type == NotificationType.PatientCalled ? "Q-007" : null,
            DoctorName = type == NotificationType.PatientCalled ? "Dr. Silva" : null,
            RoomNumber = type == NotificationType.PatientCalled ? "1" : null,
            OccurredAt = occurredAt ?? new DateTime(2026, 10, 8, 4, 30, 0, DateTimeKind.Utc),
            ReceivedAt = receivedAt ?? new DateTime(2026, 10, 8, 4, 30, 1, DateTimeKind.Utc)
        };

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class BeforeSaveInterceptor : SaveChangesInterceptor
    {
        public Func<Task>? BeforeSave { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (BeforeSave is not null)
            {
                await BeforeSave();
            }

            return result;
        }
    }
}
