using System.Reflection;
using MedicalRecordService.Data;
using MedicalRecordService.Maintenance;
using MedicalRecordService.Models.Entities;
using MedicalRecordService.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace MedicalRecordService.MigrationTests;

public sealed class MedicalRecordMigrationTests
{
    [MySqlFact]
    public async Task FreshDatabase_MigratesTwice_AndSupportsExistingRepository()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var dbContext = CreateDbContext(database.ConnectionString);

        Assert.Equal(
            MaintenanceCommandRunner.Success,
            await MaintenanceCommandRunner.MigrateAsync(dbContext));

        // Removing only the history row makes the production baseliner validate every
        // legacy-compatible column, index, foreign key, and seed before restoring it.
        await database.ExecuteAsync(
            $"DELETE FROM `__EFMigrationsHistory` WHERE MigrationId = '{LegacySchemaBaseliner.InitialMigrationId}';");
        await LegacySchemaBaseliner.BaselineIfNeededAsync(dbContext);

        Assert.Equal(ExpectedTemplates(), await ReadTemplatesAsync(database.ConnectionString));

        var consultation = CreateConsultationDraft();
        var repository = CreateRepository(database.ConnectionString);
        var result = await repository.CreateAsync(consultation);

        Assert.Equal(ConsultationPersistenceOutcome.Success, result.Outcome);
        Assert.Equal("General Consultation", result.TemplateName);

        Assert.Equal(
            MaintenanceCommandRunner.Success,
            await MaintenanceCommandRunner.MigrateAsync(dbContext));
        Assert.Equal(4, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ConsultationTemplates;"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Consultations;"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId = '{LegacySchemaBaseliner.InitialMigrationId}';"));
    }

    [MySqlFact]
    public async Task LegacyDatabase_IsBaselined_WithoutChangingExistingData()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await database.ExecuteAsync(await ReadLegacySchemaAsync());

        var repository = CreateRepository(database.ConnectionString);
        var consultation = CreateConsultationDraft();
        var result = await repository.CreateAsync(consultation);
        Assert.Equal(ConsultationPersistenceOutcome.Success, result.Outcome);

        var templatesBefore = await ReadTemplatesAsync(database.ConnectionString);
        var consultationBefore = await ReadConsultationAsync(
            database.ConnectionString,
            consultation.Id);

        await using var dbContext = CreateDbContext(database.ConnectionString);
        Assert.Equal(
            MaintenanceCommandRunner.Success,
            await MaintenanceCommandRunner.MigrateAsync(dbContext));
        Assert.Equal(
            MaintenanceCommandRunner.Success,
            await MaintenanceCommandRunner.MigrateAsync(dbContext));

        Assert.Equal(templatesBefore, await ReadTemplatesAsync(database.ConnectionString));
        Assert.Equal(
            consultationBefore,
            await ReadConsultationAsync(database.ConnectionString, consultation.Id));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId = '{LegacySchemaBaseliner.InitialMigrationId}';"));
    }

    [MySqlFact]
    public async Task IncompleteLegacyDatabase_IsRejected()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await database.ExecuteAsync(
            "CREATE TABLE ConsultationTemplates (Id CHAR(36) NOT NULL PRIMARY KEY);");
        await using var dbContext = CreateDbContext(database.ConnectionString);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => MaintenanceCommandRunner.MigrateAsync(dbContext));

        Assert.Equal(
            "The legacy medical-record schema is incomplete and cannot be baselined.",
            exception.Message);
        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES " +
                "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '__EFMigrationsHistory';"));
    }

    [Fact]
    public async Task UnavailableDatabase_ReturnsFailure_WithoutLoggingConnectionSecrets()
    {
        const string secret = "must-not-appear-in-output";
        const string variable = "ConnectionStrings__MedicalRecordDb";
        var previousValue = Environment.GetEnvironmentVariable(variable);
        var previousError = Console.Error;
        using var error = new StringWriter();

        try
        {
            Environment.SetEnvironmentVariable(
                variable,
                $"Server=127.0.0.1;Port=1;Database=unavailable;User Id=test;Password={secret};Connection Timeout=1;");
            Console.SetError(error);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            var exitCode = await MaintenanceCommandRunner.RunAsync(
                MaintenanceCommand.Migrate,
                cancellation.Token);

            Assert.Equal(MaintenanceCommandRunner.Failure, exitCode);
            Assert.Contains("MedicalRecordService migration failed", error.ToString());
            Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Password=", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetError(previousError);
            Environment.SetEnvironmentVariable(variable, previousValue);
        }
    }

    private static MedicalRecordDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MedicalRecordDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0)))
            .Options;
        return new MedicalRecordDbContext(options);
    }

    private static AdoNetConsultationRepository CreateRepository(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MedicalRecordDb"] = connectionString
            })
            .Build();
        return new AdoNetConsultationRepository(
            new MySqlMedicalRecordConnectionFactory(configuration));
    }

    private static ConsultationDraft CreateConsultationDraft()
    {
        return new ConsultationDraft
        {
            Id = Guid.NewGuid(),
            QueueId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            DoctorId = Guid.NewGuid(),
            DoctorName = "Dr. Migration Test",
            RoomNumber = "R-107",
            Symptoms = "Migration test symptoms",
            ExaminationFindings = "Migration test findings",
            Diagnosis = "Migration test diagnosis",
            Notes = "Migration test notes",
            TemplateId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            ConsultationDate = new DateTime(2026, 9, 16, 8, 30, 0, DateTimeKind.Utc)
        };
    }

    private static async Task<string> ReadLegacySchemaAsync()
    {
        const string resourceName =
            "MedicalRecordService.MigrationTests.Fixtures.LegacyMedicalRecordSchema.sql";
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The legacy schema test fixture is missing.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static async Task<IReadOnlyList<TemplateSnapshot>> ReadTemplatesAsync(
        string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Symptoms, ExaminationFindings, Notes, IsActive, CreatedAt
            FROM ConsultationTemplates
            ORDER BY Id;
            """;
        await using var reader = await command.ExecuteReaderAsync();

        var templates = new List<TemplateSnapshot>();
        while (await reader.ReadAsync())
        {
            templates.Add(new TemplateSnapshot(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.GetDateTime(6)));
        }

        return templates;
    }

    private static IReadOnlyList<TemplateSnapshot> ExpectedTemplates()
    {
        var createdAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        return
        [
            new(
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                "General Consultation",
                "Presenting symptoms:\n- ",
                "Examination findings:\n- ",
                "Assessment and plan:\n- ",
                true,
                createdAt),
            new(
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                "Respiratory Consultation",
                "Respiratory symptoms:\n- ",
                "Respiratory examination findings:\n- ",
                "Respiratory assessment and plan:\n- ",
                true,
                createdAt),
            new(
                Guid.Parse("00000000-0000-0000-0000-000000000003"),
                "Gastrointestinal Consultation",
                "Gastrointestinal symptoms:\n- ",
                "Abdominal examination findings:\n- ",
                "Gastrointestinal assessment and plan:\n- ",
                true,
                createdAt),
            new(
                Guid.Parse("00000000-0000-0000-0000-000000000004"),
                "Musculoskeletal Consultation",
                "Musculoskeletal symptoms:\n- ",
                "Musculoskeletal examination findings:\n- ",
                "Musculoskeletal assessment and plan:\n- ",
                true,
                createdAt)
        ];
    }

    private static async Task<ConsultationSnapshot> ReadConsultationAsync(
        string connectionString,
        Guid consultationId)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, PatientId, QueueId, DoctorId, DoctorName, RoomNumber, Symptoms,
                   ExaminationFindings, Diagnosis, Notes, TemplateId, TemplateName,
                   ConsultationDate, CreatedAt
            FROM Consultations
            WHERE Id = @Id;
            """;
        command.Parameters.AddWithValue("@Id", consultationId.ToString());
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        return new ConsultationSnapshot(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetGuid(10),
            reader.GetString(11),
            reader.GetDateTime(12),
            reader.GetDateTime(13));
    }

    private sealed record TemplateSnapshot(
        Guid Id,
        string Name,
        string Symptoms,
        string ExaminationFindings,
        string Notes,
        bool IsActive,
        DateTime CreatedAt);

    private sealed record ConsultationSnapshot(
        Guid Id,
        Guid PatientId,
        Guid QueueId,
        Guid DoctorId,
        string DoctorName,
        string RoomNumber,
        string Symptoms,
        string ExaminationFindings,
        string Diagnosis,
        string Notes,
        Guid TemplateId,
        string TemplateName,
        DateTime ConsultationDate,
        DateTime CreatedAt);
}
