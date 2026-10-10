using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MySqlConnector;
using PatientService.Data;
using PatientService.Models.Entities;
using PatientService.Models.Events;
using PatientService.Services;

namespace PatientService.UnitTests.Services;

public sealed class OutboxMySqlTests
{
    [MySqlFact]
    public async Task MigrationAndRestartRetainAnUnacknowledgedEvent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var payload = new PatientCheckedInEvent
        {
            EventId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            IsNewPatient = true,
            CheckedInAt = DateTime.UtcNow,
            CorrelationId = "restart-test"
        };
        await using (var context = database.CreateContext())
        {
            context.OutboxMessages.Add(OutboxMessage.Create(payload.EventId, payload, DateTime.UtcNow));
            await context.SaveChangesAsync();
        }
        var publisher = new Mock<IPatientEventPublisher>();
        publisher.Setup(item => item.PublishAsync(It.IsAny<PatientCheckedInEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PatientCheckedInEvent, CancellationToken>((actual, _) =>
                Assert.Equal(JsonSerializer.Serialize(payload), JsonSerializer.Serialize(actual))).ReturnsAsync(true);
        await using var restarted = database.CreateContext();
        Assert.Equal(1, await OutboxDelivery.DeliverPendingAsync(restarted, publisher.Object, NullLogger.Instance));
        Assert.Empty(await restarted.OutboxMessages.ToListAsync());
    }

    [MySqlFact]
    public async Task MissingOutboxTableRollsBackPatientInsertWithoutReportingDuplicateNic()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync("DROP TABLE OutboxMessages");
        var publisher = new Mock<IPatientEventPublisher>(MockBehavior.Strict);
        var service = new PatientRegistrationService(context, publisher.Object, NullLogger<PatientRegistrationService>.Instance);
        await Assert.ThrowsAsync<DbUpdateException>(() => service.RegisterPatientAsync(new PatientService.Models.Dtos.RegisterPatientRequest
        {
            Nic = "199012345678",
            FullName = "Test Patient",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = PatientService.Models.Enums.Gender.Male,
            Address = "Test Address",
            PhoneNumber = "0771234567",
            BloodGroup = PatientService.Models.Enums.BloodGroup.APositive
        }, "atomic-save", Guid.NewGuid()));
        await using var reader = database.CreateContext();
        Assert.Empty(await reader.Patients.ToListAsync());
        publisher.VerifyNoOtherCalls();
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
        public PatientDbContext CreateContext() => new(new DbContextOptionsBuilder<PatientDbContext>()
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
