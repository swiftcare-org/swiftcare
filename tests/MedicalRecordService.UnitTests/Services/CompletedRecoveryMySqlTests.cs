using MedicalRecordService.Data;
using MedicalRecordService.Models.Entities;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace MedicalRecordService.UnitTests.Services;

public class CompletedRecoveryMySqlTests
{
    [MySqlRecoveryFact]
    public async Task CompletedPagesAreOrderedAndScopedToTheDoctor()
    {
        var settings = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SWIFTCARE_TEST_MYSQL")!);
        var name = "swc_rc_" + Guid.NewGuid().ToString("N");
        await using var admin = new MySqlConnection(settings.ConnectionString);
        await admin.OpenAsync();
        await using (var command = new MySqlCommand($"CREATE DATABASE `{name}`", admin)) await command.ExecuteNonQueryAsync();
        settings.Database = name;
        try
        {
            await using var context = new MedicalRecordDbContext(new DbContextOptionsBuilder<MedicalRecordDbContext>()
                .UseMySql(settings.ConnectionString, new MySqlServerVersion(new Version(8, 4, 0))).Options);
            await context.Database.MigrateAsync();
            var doctor = Guid.NewGuid();
            var otherDoctor = Guid.NewGuid();
            var visits = Enumerable.Range(0, 51).Select(i => Visit(doctor, i)).ToArray();
            var incomplete = Visit(doctor, -1);
            incomplete.Status = Consultation.InProgressStatus;
            context.Consultations.AddRange(visits);
            context.Consultations.AddRange(incomplete, Visit(otherDoctor, -2));
            await context.SaveChangesAsync();
            var repository = new AdoNetConsultationCompletionRepository(new ConnectionFactory(settings.ConnectionString));

            var first = await repository.FindCompletedPageAsync(doctor, 0);
            var second = await repository.FindCompletedPageAsync(doctor, 1);

            Assert.Equal(visits.Take(50).Select(visit => visit.Id), first.Select(visit => visit.ConsultationId));
            Assert.Equal(visits[50].Id, Assert.Single(second).ConsultationId);
            Assert.Single(await repository.FindCompletedPageAsync(otherDoctor, 0));
            Assert.Empty(await repository.FindCompletedPageAsync(doctor, 2));
        }
        finally
        {
            await using var command = new MySqlCommand($"DROP DATABASE `{name}`", admin);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static Consultation Visit(Guid doctor, int minute) => new()
    {
        Id = Guid.NewGuid(),
        QueueId = Guid.NewGuid(),
        PatientId = Guid.NewGuid(),
        DoctorId = doctor,
        DoctorName = "Dr. Test",
        RoomNumber = "1",
        Symptoms = "Synthetic",
        Diagnosis = "Synthetic",
        Status = Consultation.CompleteStatus,
        ConsultationDate = new DateTime(2026, 10, 10, 6, 0, 0).AddMinutes(minute),
        CreatedAt = new DateTime(2026, 10, 10, 6, 0, 0).AddMinutes(minute)
    };

    private sealed class ConnectionFactory(string value) : IMedicalRecordConnectionFactory
    {
        public async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new MySqlConnection(value);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }
}

public sealed class MySqlRecoveryFactAttribute : FactAttribute
{
    public MySqlRecoveryFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SWIFTCARE_TEST_MYSQL")))
            Skip = "Set SWIFTCARE_TEST_MYSQL to an isolated MySQL server to verify completed visit paging.";
    }
}
