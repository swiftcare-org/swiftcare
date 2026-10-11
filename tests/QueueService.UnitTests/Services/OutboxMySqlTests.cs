using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MySqlConnector;
using QueueService.Data;
using QueueService.Models.Entities;
using QueueService.Models.Events;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

public sealed class OutboxMySqlTests
{
    [MySqlFact]
    public async Task MigrationAndRestartRetainAnUnacknowledgedEvent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var payload = new PatientCalledEvent
        {
            EventId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            QueueId = Guid.NewGuid(),
            QueueNumber = "Q-001",
            DoctorId = Guid.NewGuid(),
            DoctorName = "Doctor",
            RoomNumber = "R-1",
            CalledAt = DateTime.UtcNow,
            CorrelationId = "restart-test"
        };
        await using (var context = database.CreateContext())
        {
            context.OutboxMessages.Add(OutboxMessage.Create(payload.EventId, payload, DateTime.UtcNow));
            await context.SaveChangesAsync();
        }
        var publisher = new Mock<IQueueEventPublisher>();
        publisher.Setup(item => item.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PatientCalledEvent, CancellationToken>((actual, _) =>
                Assert.Equal(JsonSerializer.Serialize(payload), JsonSerializer.Serialize(actual))).ReturnsAsync(true);
        await using var restarted = database.CreateContext();
        Assert.Equal(1, await OutboxDelivery.DeliverPendingAsync(restarted, publisher.Object, NullLogger.Instance));
        Assert.Empty(await restarted.OutboxMessages.ToListAsync());
    }

    private sealed class MySqlFactAttribute : FactAttribute
    {
        public MySqlFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SWIFTCARE_TEST_MYSQL")))
                Skip = "Set SWIFTCARE_TEST_MYSQL to run isolated database regressions.";
        }
    }

    private sealed class TestDatabase(string databaseName, string connectionString, string adminConnectionString) : IAsyncDisposable
    {
        public QueueDbContext CreateContext() => new(new DbContextOptionsBuilder<QueueDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0))).Options);

        public static async Task<TestDatabase> CreateAsync()
        {
            var builder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SWIFTCARE_TEST_MYSQL"));
            builder.Database = "";
            var admin = builder.ConnectionString;
            var name = "outbox_" + Guid.NewGuid().ToString("N");
            await using var connection = new MySqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new MySqlCommand($"CREATE DATABASE `{name}`", connection);
            await command.ExecuteNonQueryAsync();
            builder.Database = name;
            var database = new TestDatabase(name, builder.ConnectionString, admin);
            try
            {
                await using var context = database.CreateContext();
                await context.Database.MigrateAsync();
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new MySqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand($"DROP DATABASE IF EXISTS `{databaseName}`", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
