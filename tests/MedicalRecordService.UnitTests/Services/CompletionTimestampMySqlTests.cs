using MedicalRecordService.Data;
using MedicalRecordService.Models.Entities;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace MedicalRecordService.UnitTests.Services;

public class CompletionTimestampMySqlTests
{
    [MySqlCompletionFact]
    public async Task MigrationAndCompletionRetriesPreserveTheOriginalTimestamp()
    {
        var settings = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SWIFTCARE_TEST_MYSQL")!);
        var database = "swc_ct_" + Guid.NewGuid().ToString("N");
        await using var admin = new MySqlConnection(settings.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new MySqlCommand($"CREATE DATABASE `{database}`", admin))
            await create.ExecuteNonQueryAsync();
        settings.Database = database;
        try
        {
            await using var context = new MedicalRecordDbContext(new DbContextOptionsBuilder<MedicalRecordDbContext>()
                .UseMySql(settings.ConnectionString, new MySqlServerVersion(new Version(8, 4, 0))).Options);
            await context.Database.MigrateAsync();
            var consultation = new Consultation
            {
                Id = Guid.NewGuid(),
                QueueId = Guid.NewGuid(),
                PatientId = Guid.NewGuid(),
                DoctorId = Guid.NewGuid(),
                DoctorName = "Dr. Test",
                RoomNumber = "1",
                Symptoms = "Synthetic",
                Diagnosis = "Synthetic",
                ConsultationDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            context.Consultations.Add(consultation);
            context.VitalSigns.Add(new VitalSigns { Id = Guid.NewGuid(), ConsultationId = consultation.Id, RecordedAt = DateTime.UtcNow });
            await context.SaveChangesAsync();
            var clock = new MutableClock();
            var repository = new AdoNetConsultationCompletionRepository(new ConnectionFactory(settings.ConnectionString), clock);

            var first = await repository.PrepareAsync(consultation.Id, consultation.DoctorId);
            clock.Now = clock.Now.AddDays(2);
            var retry = await repository.PrepareAsync(consultation.Id, consultation.DoctorId);

            Assert.NotNull(first.Event);
            Assert.Equal(first.Event, retry.Event);
            Assert.Equal(new DateTime(2026, 9, 30, 18, 29, 0, DateTimeKind.Utc), retry.Event!.CompletedAt);
        }
        finally
        {
            await using var drop = new MySqlCommand($"DROP DATABASE `{database}`", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class ConnectionFactory(string connectionString) : IMedicalRecordConnectionFactory
    {
        public async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 30, 18, 29, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

public sealed class MySqlCompletionFactAttribute : FactAttribute
{
    public MySqlCompletionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SWIFTCARE_TEST_MYSQL")))
            Skip = "Set SWIFTCARE_TEST_MYSQL to an isolated MySQL server to run database verification.";
    }
}
