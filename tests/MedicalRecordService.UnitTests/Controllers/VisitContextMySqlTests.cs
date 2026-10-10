using MedicalRecordService.Data;
using MedicalRecordService.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace MedicalRecordService.UnitTests.Controllers;

public sealed class VisitContextMySqlTests
{
    [MySqlTheory]
    [InlineData("COMPLETE")]
    [InlineData("IN_PROGRESS")]
    public async Task LookupReturnsAuthoritativeIdentifiersAndStatus(string status)
    {
        var builder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SWIFTCARE_TEST_MYSQL")!);
        builder.Database = "";
        await using var admin = new MySqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        var name = "visit_" + Guid.NewGuid().ToString("N");
        await using (var create = new MySqlCommand($"CREATE DATABASE `{name}`", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            builder.Database = name;
            var visit = new Consultation
            {
                Id = Guid.NewGuid(),
                PatientId = Guid.NewGuid(),
                QueueId = Guid.NewGuid(),
                DoctorId = Guid.NewGuid(),
                DoctorName = "Test Doctor",
                RoomNumber = "R-1",
                Symptoms = "Test symptoms",
                Diagnosis = "Test diagnosis",
                Status = status,
                ConsultationDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            await using (var context = new MedicalRecordDbContext(new DbContextOptionsBuilder<MedicalRecordDbContext>()
                .UseMySql(builder.ConnectionString, new MySqlServerVersion(new Version(8, 4, 0))).Options))
            {
                await context.Database.MigrateAsync();
                context.Consultations.Add(visit);
                await context.SaveChangesAsync();
            }
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:MedicalRecordDb"] = builder.ConnectionString }).Build();
            var repository = new AdoNetVisitContextRepository(new MySqlMedicalRecordConnectionFactory(configuration));
            Assert.Null(await repository.FindAsync(Guid.NewGuid(), CancellationToken.None));
            var found = await repository.FindAsync(visit.Id, CancellationToken.None);
            Assert.NotNull(found);
            Assert.Equal(visit.PatientId, found.PatientId);
            Assert.Equal(visit.QueueId, found.QueueId);
            Assert.Equal(visit.DoctorId, found.DoctorId);
            Assert.Equal(status, found.Status);
        }
        finally
        {
            await using var drop = new MySqlCommand($"DROP DATABASE IF EXISTS `{name}`", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class MySqlTheoryAttribute : TheoryAttribute
    {
        public MySqlTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SWIFTCARE_TEST_MYSQL")))
                Skip = "Set SWIFTCARE_TEST_MYSQL to run isolated database regressions.";
        }
    }
}
