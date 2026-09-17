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
    private const string InitialMigrationId = "20260916082514_InitialMedicalRecordSchema";

    [MySqlFact]
    public async Task FreshDatabase_MigratesTwice_AndSupportsExistingRepository()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var dbContext = CreateDbContext(database.ConnectionString);

        Assert.Equal(
            MaintenanceCommandRunner.Success,
            await MaintenanceCommandRunner.MigrateAsync(dbContext));

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
                $"SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId = '{InitialMigrationId}';"));
    }

    [MySqlFact]
    public async Task FreshDatabase_CreatesExpectedSchema()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var dbContext = CreateDbContext(database.ConnectionString);

        Assert.Equal(
            MaintenanceCommandRunner.Success,
            await MaintenanceCommandRunner.MigrateAsync(dbContext));

        var columns = await ReadColumnsAsync(database.ConnectionString);
        Assert.Equal(21, columns.Count);
        AssertColumn(columns, "ConsultationTemplates.Id", "char(36)", false, "ascii", "ascii_bin");
        AssertColumn(columns, "ConsultationTemplates.Name", "varchar(150)", false, "utf8mb4");
        AssertColumn(columns, "ConsultationTemplates.IsActive", "tinyint(1)", false);
        AssertColumn(columns, "Consultations.Id", "char(36)", false, "ascii", "ascii_bin");
        AssertColumn(columns, "Consultations.PatientId", "char(36)", false, "ascii", "ascii_bin");
        AssertColumn(columns, "Consultations.QueueId", "char(36)", false, "ascii", "ascii_bin");
        AssertColumn(columns, "Consultations.DoctorId", "char(36)", false, "ascii", "ascii_bin");
        AssertColumn(columns, "Consultations.DoctorName", "varchar(200)", false, "utf8mb4");
        AssertColumn(columns, "Consultations.RoomNumber", "varchar(50)", false, "utf8mb4");
        AssertColumn(columns, "Consultations.ExaminationFindings", "text", true, "utf8mb4");
        AssertColumn(columns, "Consultations.Notes", "text", true, "utf8mb4");
        AssertColumn(columns, "Consultations.TemplateId", "char(36)", true, "ascii", "ascii_bin");
        AssertColumn(columns, "Consultations.TemplateName", "varchar(150)", true, "utf8mb4");
        AssertColumn(columns, "Consultations.ConsultationDate", "datetime(6)", false);

        // Seven indexes are declared by the migration; MySQL creates the eighth
        // automatically to support the TemplateId foreign key.
        Assert.Equal(
            8,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS " +
                "WHERE TABLE_SCHEMA = DATABASE() " +
                "AND TABLE_NAME IN ('ConsultationTemplates', 'Consultations');"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS " +
                "WHERE CONSTRAINT_SCHEMA = DATABASE() " +
                "AND CONSTRAINT_NAME = 'FK_Consultations_ConsultationTemplates_TemplateId' " +
                "AND DELETE_RULE = 'RESTRICT';"));
    }

    [MySqlFact]
    public async Task ExistingSchemaWithoutMigrationHistory_IsNotAutomaticallyBaselined()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await database.ExecuteAsync(
            "CREATE TABLE ConsultationTemplates (Id CHAR(36) NOT NULL PRIMARY KEY);");

        await using var dbContext = CreateDbContext(database.ConnectionString);
        await Assert.ThrowsAnyAsync<Exception>(
            () => MaintenanceCommandRunner.MigrateAsync(dbContext));

        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId = '{InitialMigrationId}';"));
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

    private static async Task<IReadOnlyDictionary<string, ColumnSnapshot>> ReadColumnsAsync(
        string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE,
                   CHARACTER_SET_NAME, COLLATION_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME IN ('ConsultationTemplates', 'Consultations');
            """;
        await using var reader = await command.ExecuteReaderAsync();

        var columns = new Dictionary<string, ColumnSnapshot>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            columns.Add(
                $"{reader.GetString(0)}.{reader.GetString(1)}",
                new ColumnSnapshot(
                    reader.GetString(2),
                    string.Equals(reader.GetString(3), "YES", StringComparison.Ordinal),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return columns;
    }

    private static void AssertColumn(
        IReadOnlyDictionary<string, ColumnSnapshot> columns,
        string key,
        string columnType,
        bool nullable,
        string? characterSet = null,
        string? collation = null)
    {
        Assert.True(columns.TryGetValue(key, out var actual), $"Missing expected column {key}.");
        Assert.Equal(columnType, actual.ColumnType, ignoreCase: true);
        Assert.Equal(nullable, actual.IsNullable);
        if (characterSet is not null)
        {
            Assert.Equal(characterSet, actual.CharacterSet, ignoreCase: true);
        }
        if (collation is not null)
        {
            Assert.Equal(collation, actual.Collation, ignoreCase: true);
        }
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

    private sealed record TemplateSnapshot(
        Guid Id,
        string Name,
        string Symptoms,
        string ExaminationFindings,
        string Notes,
        bool IsActive,
        DateTime CreatedAt);

    private sealed record ColumnSnapshot(
        string ColumnType,
        bool IsNullable,
        string? CharacterSet,
        string? Collation);

}
