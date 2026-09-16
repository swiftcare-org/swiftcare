using MedicalRecordService.Data;
using MedicalRecordService.Models.Entities;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace MedicalRecordService.Maintenance;

public static class LegacySchemaBaseliner
{
    public const string InitialMigrationId = "20260916082514_InitialMedicalRecordSchema";
    private const string EfProductVersion = "9.0.0";
    private const string HistoryTable = "__EFMigrationsHistory";
    private const string ConsultationTemplatesTable = "ConsultationTemplates";
    private const string ConsultationsTable = "Consultations";

    private static readonly IReadOnlyDictionary<string, ExpectedColumn> ExpectedColumns =
        CreateExpectedColumns();

    private static readonly IReadOnlyList<ExpectedIndex> ExpectedIndexes =
    [
        new(ConsultationTemplatesTable, "PRIMARY", true, "Id"),
        new(ConsultationTemplatesTable, "UX_ConsultationTemplates_Name", true, "Name"),
        new(ConsultationsTable, "PRIMARY", true, "Id"),
        new(ConsultationsTable, "UX_Consultations_QueueId", true, "QueueId"),
        new(ConsultationsTable, "IX_Consultations_PatientId", false, "PatientId"),
        new(ConsultationsTable, "IX_Consultations_DoctorId", false, "DoctorId"),
        new(ConsultationsTable, "IX_Consultations_ConsultationDate", false, "ConsultationDate")
    ];

    public static async Task BaselineIfNeededAsync(
        MedicalRecordDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        var connection = (MySqlConnection)dbContext.Database.GetDbConnection();

        try
        {
            var historyExists = await TableExistsAsync(connection, HistoryTable, cancellationToken);
            var appliedMigrations = historyExists
                ? await ReadAppliedMigrationsAsync(connection, cancellationToken)
                : [];

            if (appliedMigrations.Contains(InitialMigrationId))
            {
                return;
            }

            if (appliedMigrations.Count != 0)
            {
                throw new InvalidOperationException(
                    "EF migration history contains later entries without the initial migration.");
            }

            var templatesExist = await TableExistsAsync(
                connection,
                ConsultationTemplatesTable,
                cancellationToken);
            var consultationsExist = await TableExistsAsync(
                connection,
                ConsultationsTable,
                cancellationToken);

            if (!templatesExist && !consultationsExist)
            {
                // Empty database: EF applies the initial migration normally.
                return;
            }

            if (!templatesExist || !consultationsExist)
            {
                throw new InvalidOperationException(
                    "The legacy medical-record schema is incomplete and cannot be baselined.");
            }

            await ValidateColumnsAsync(connection, cancellationToken);
            await ValidateIndexesAsync(connection, cancellationToken);
            await ValidateForeignKeyAsync(connection, cancellationToken);
            await ValidateTemplateSeedsAsync(connection, cancellationToken);
            await RecordBaselineAsync(connection, cancellationToken);

            Console.WriteLine(
                $"Validated the legacy medical-record schema and recorded baseline {InitialMigrationId}.");
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> TableExistsAsync(
        MySqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @TableName
              AND TABLE_TYPE = 'BASE TABLE';
            """;
        command.Parameters.AddWithValue("@TableName", tableName);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<HashSet<string>> ReadAppliedMigrationsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT MigrationId FROM `{HistoryTable}`;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var migrations = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            migrations.Add(reader.GetString(0));
        }

        return migrations;
    }

    private static async Task ValidateColumnsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE,
                   CHARACTER_SET_NAME, COLLATION_NAME, COLUMN_DEFAULT
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME IN ('ConsultationTemplates', 'Consultations');
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var actual = new Dictionary<string, ActualColumn>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = reader.GetString(0);
            var name = reader.GetString(1);
            actual[ColumnKey(table, name)] = new ActualColumn(
                reader.GetString(2),
                string.Equals(reader.GetString(3), "YES", StringComparison.Ordinal),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetValue(6).ToString());
        }

        if (actual.Count != ExpectedColumns.Count)
        {
            throw new InvalidOperationException(
                "The legacy medical-record schema has an unexpected column count.");
        }

        foreach (var (key, expected) in ExpectedColumns)
        {
            if (!actual.TryGetValue(key, out var column) || !expected.Matches(column))
            {
                throw new InvalidOperationException(
                    "The legacy medical-record schema has an incompatible column definition.");
            }
        }
    }

    private static async Task ValidateIndexesAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TABLE_NAME, INDEX_NAME, NON_UNIQUE, SEQ_IN_INDEX, COLUMN_NAME
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME IN ('ConsultationTemplates', 'Consultations')
            ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var indexes = new Dictionary<string, ActualIndex>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = reader.GetString(0);
            var name = reader.GetString(1);
            var key = IndexKey(table, name);
            if (!indexes.TryGetValue(key, out var index))
            {
                index = new ActualIndex(reader.GetInt64(2) == 0, []);
                indexes.Add(key, index);
            }

            index.Columns.Add(reader.GetString(4));
        }

        foreach (var expected in ExpectedIndexes)
        {
            if (!indexes.TryGetValue(IndexKey(expected.Table, expected.Name), out var index) ||
                index.IsUnique != expected.IsUnique ||
                index.Columns.Count != 1 ||
                !string.Equals(index.Columns[0], expected.Column, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The legacy medical-record schema has a missing or incompatible index.");
            }
        }
    }

    private static async Task ValidateForeignKeyAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT k.CONSTRAINT_NAME, k.COLUMN_NAME, k.REFERENCED_TABLE_NAME,
                   k.REFERENCED_COLUMN_NAME, r.DELETE_RULE
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS k
            INNER JOIN INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS AS r
                ON r.CONSTRAINT_SCHEMA = k.CONSTRAINT_SCHEMA
               AND r.CONSTRAINT_NAME = k.CONSTRAINT_NAME
               AND r.TABLE_NAME = k.TABLE_NAME
            WHERE k.TABLE_SCHEMA = DATABASE()
              AND k.TABLE_NAME = 'Consultations'
              AND k.REFERENCED_TABLE_NAME IS NOT NULL;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken) ||
            !string.Equals(
                reader.GetString(0),
                "FK_Consultations_ConsultationTemplates_TemplateId",
                StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(1), "TemplateId", StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(2), ConsultationTemplatesTable, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(3), "Id", StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(4), "RESTRICT", StringComparison.OrdinalIgnoreCase) ||
            await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The legacy medical-record schema has an incompatible template foreign key.");
        }
    }

    private static async Task ValidateTemplateSeedsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Symptoms, ExaminationFindings, Notes, IsActive
            FROM ConsultationTemplates
            WHERE Id IN (
                '00000000-0000-0000-0000-000000000001',
                '00000000-0000-0000-0000-000000000002',
                '00000000-0000-0000-0000-000000000003',
                '00000000-0000-0000-0000-000000000004');
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var templates = ConsultationTemplateSeedData.Templates.ToDictionary(template => template.Id);
        var matched = new HashSet<Guid>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            if (!templates.TryGetValue(id, out var expected) ||
                !TemplateMatches(reader, expected))
            {
                throw new InvalidOperationException(
                    "A required legacy consultation template is incompatible.");
            }

            matched.Add(id);
        }

        if (matched.Count != templates.Count)
        {
            throw new InvalidOperationException(
                "One or more required legacy consultation templates are missing.");
        }
    }

    private static bool TemplateMatches(
        MySqlDataReader reader,
        ConsultationTemplate expected)
    {
        return string.Equals(reader.GetString(1), expected.Name, StringComparison.Ordinal) &&
               string.Equals(reader.GetString(2), expected.Symptoms, StringComparison.Ordinal) &&
               string.Equals(reader.GetString(3), expected.ExaminationFindings, StringComparison.Ordinal) &&
               string.Equals(reader.GetString(4), expected.Notes, StringComparison.Ordinal) &&
               reader.GetBoolean(5) == expected.IsActive;
    }

    private static async Task RecordBaselineAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var createHistory = connection.CreateCommand())
        {
            createHistory.CommandText = $"""
                CREATE TABLE IF NOT EXISTS `{HistoryTable}` (
                    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
                    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
                    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
                ) CHARACTER SET=utf8mb4;
                """;
            await createHistory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insertHistory = connection.CreateCommand();
        insertHistory.CommandText = $"""
            INSERT IGNORE INTO `{HistoryTable}` (`MigrationId`, `ProductVersion`)
            VALUES (@MigrationId, @ProductVersion);
            """;
        insertHistory.Parameters.AddWithValue("@MigrationId", InitialMigrationId);
        insertHistory.Parameters.AddWithValue("@ProductVersion", EfProductVersion);
        await insertHistory.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyDictionary<string, ExpectedColumn> CreateExpectedColumns()
    {
        var columns = new Dictionary<string, ExpectedColumn>(StringComparer.Ordinal);

        Add(columns, ConsultationTemplatesTable, "Id", "char(36)", false, "ascii", "ascii_bin");
        Add(columns, ConsultationTemplatesTable, "Name", "varchar(150)", false, "utf8mb4");
        Add(columns, ConsultationTemplatesTable, "Symptoms", "text", false, "utf8mb4");
        Add(columns, ConsultationTemplatesTable, "ExaminationFindings", "text", false, "utf8mb4");
        Add(columns, ConsultationTemplatesTable, "Notes", "text", false, "utf8mb4");
        Add(columns, ConsultationTemplatesTable, "IsActive", "tinyint(1)", false, defaultValue: "1");
        Add(columns, ConsultationTemplatesTable, "CreatedAt", "datetime(6)", false);

        Add(columns, ConsultationsTable, "Id", "char(36)", false, "ascii", "ascii_bin");
        Add(columns, ConsultationsTable, "PatientId", "char(36)", false, "ascii", "ascii_bin");
        Add(columns, ConsultationsTable, "QueueId", "char(36)", false, "ascii", "ascii_bin");
        Add(columns, ConsultationsTable, "DoctorId", "char(36)", false, "ascii", "ascii_bin");
        Add(columns, ConsultationsTable, "DoctorName", "varchar(200)", false, "utf8mb4");
        Add(columns, ConsultationsTable, "RoomNumber", "varchar(50)", false, "utf8mb4");
        Add(columns, ConsultationsTable, "Symptoms", "text", false, "utf8mb4");
        Add(columns, ConsultationsTable, "ExaminationFindings", "text", true, "utf8mb4");
        Add(columns, ConsultationsTable, "Diagnosis", "text", false, "utf8mb4");
        Add(columns, ConsultationsTable, "Notes", "text", true, "utf8mb4");
        Add(columns, ConsultationsTable, "TemplateId", "char(36)", true, "ascii", "ascii_bin");
        Add(columns, ConsultationsTable, "TemplateName", "varchar(150)", true, "utf8mb4");
        Add(columns, ConsultationsTable, "ConsultationDate", "datetime(6)", false);
        Add(columns, ConsultationsTable, "CreatedAt", "datetime(6)", false);

        return columns;
    }

    private static void Add(
        IDictionary<string, ExpectedColumn> columns,
        string table,
        string name,
        string columnType,
        bool isNullable,
        string? characterSet = null,
        string? collation = null,
        string? defaultValue = null)
    {
        columns.Add(
            ColumnKey(table, name),
            new ExpectedColumn(columnType, isNullable, characterSet, collation, defaultValue));
    }

    private static string ColumnKey(string table, string column) => $"{table}.{column}";
    private static string IndexKey(string table, string index) => $"{table}.{index}";

    private sealed record ExpectedColumn(
        string ColumnType,
        bool IsNullable,
        string? CharacterSet,
        string? Collation,
        string? DefaultValue)
    {
        public bool Matches(ActualColumn actual)
        {
            return string.Equals(ColumnType, actual.ColumnType, StringComparison.OrdinalIgnoreCase) &&
                   IsNullable == actual.IsNullable &&
                   (CharacterSet is null || string.Equals(
                       CharacterSet,
                       actual.CharacterSet,
                       StringComparison.OrdinalIgnoreCase)) &&
                   (Collation is null || string.Equals(
                       Collation,
                       actual.Collation,
                       StringComparison.OrdinalIgnoreCase)) &&
                   (DefaultValue is null || string.Equals(
                       DefaultValue,
                       actual.DefaultValue,
                       StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed record ActualColumn(
        string ColumnType,
        bool IsNullable,
        string? CharacterSet,
        string? Collation,
        string? DefaultValue);

    private sealed record ExpectedIndex(
        string Table,
        string Name,
        bool IsUnique,
        string Column);

    private sealed record ActualIndex(bool IsUnique, List<string> Columns);
}
