using MedicalRecordService.Models.Entities;
using MySqlConnector;

namespace MedicalRecordService.Data;

public sealed class AdoNetConsultationTemplateRepository : IConsultationTemplateRepository
{
    private readonly IMedicalRecordConnectionFactory _connectionFactory;

    public AdoNetConsultationTemplateRepository(IMedicalRecordConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ConsultationTemplate>> ListVisibleToDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Symptoms, ExaminationFindings, Notes, CreatedByDoctorId, IsActive
            FROM ConsultationTemplates
            WHERE IsActive = TRUE
              AND (CreatedByDoctorId IS NULL OR CreatedByDoctorId = @DoctorId)
            ORDER BY CreatedByDoctorId IS NOT NULL, Name;
            """;
        command.Parameters.Add("@DoctorId", MySqlDbType.VarChar, 36).Value = doctorId.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var templates = new List<ConsultationTemplate>();

        while (await reader.ReadAsync(cancellationToken))
        {
            templates.Add(ReadTemplate(reader));
        }

        return templates;
    }

    public async Task<bool> TryAddAsync(
        ConsultationTemplate template,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ConsultationTemplates
                (Id, Name, Symptoms, ExaminationFindings, Notes, IsActive, CreatedAt, CreatedByDoctorId)
            VALUES
                (@Id, @Name, @Symptoms, @ExaminationFindings, @Notes, TRUE, @CreatedAt, @CreatedByDoctorId);
            """;
        command.Parameters.Add("@Id", MySqlDbType.VarChar, 36).Value = template.Id.ToString();
        command.Parameters.Add("@Name", MySqlDbType.VarChar, 150).Value = template.Name;
        command.Parameters.Add("@Symptoms", MySqlDbType.Text).Value = template.Symptoms;
        command.Parameters.Add("@ExaminationFindings", MySqlDbType.Text).Value = template.ExaminationFindings;
        command.Parameters.Add("@Notes", MySqlDbType.Text).Value = template.Notes;
        command.Parameters.Add("@CreatedAt", MySqlDbType.DateTime).Value = template.CreatedAt;
        command.Parameters.Add("@CreatedByDoctorId", MySqlDbType.VarChar, 36).Value =
            template.CreatedByDoctorId.HasValue ? template.CreatedByDoctorId.Value.ToString() : DBNull.Value;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            // UX_ConsultationTemplates_ActiveOwnerScope_Name: this owner already has an
            // active template with the same name.
            return false;
        }
    }

    public async Task<ConsultationTemplate?> FindAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Symptoms, ExaminationFindings, Notes, CreatedByDoctorId, IsActive
            FROM ConsultationTemplates
            WHERE Id = @Id
            LIMIT 1;
            """;
        command.Parameters.Add("@Id", MySqlDbType.VarChar, 36).Value = templateId.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTemplate(reader) : null;
    }

    public async Task<bool> DeactivateAsync(
        Guid templateId,
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // The owner is part of the WHERE clause, so even a caller that skipped the
        // ownership check could not deactivate a built-in or another doctor's template.
        command.CommandText = """
            UPDATE ConsultationTemplates
            SET IsActive = FALSE
            WHERE Id = @Id AND CreatedByDoctorId = @DoctorId AND IsActive = TRUE;
            """;
        command.Parameters.Add("@Id", MySqlDbType.VarChar, 36).Value = templateId.ToString();
        command.Parameters.Add("@DoctorId", MySqlDbType.VarChar, 36).Value = doctorId.ToString();

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static ConsultationTemplate ReadTemplate(MySqlDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        Symptoms = reader.GetString(reader.GetOrdinal("Symptoms")),
        ExaminationFindings = reader.GetString(reader.GetOrdinal("ExaminationFindings")),
        Notes = reader.GetString(reader.GetOrdinal("Notes")),
        CreatedByDoctorId = reader.IsDBNull(reader.GetOrdinal("CreatedByDoctorId"))
            ? null
            : reader.GetGuid(reader.GetOrdinal("CreatedByDoctorId")),
        IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive"))
    };
}
