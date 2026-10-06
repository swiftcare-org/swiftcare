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
            SELECT Id, Name, Symptoms, ExaminationFindings, Notes, CreatedByDoctorId
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

    private static ConsultationTemplate ReadTemplate(MySqlDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        Symptoms = reader.GetString(reader.GetOrdinal("Symptoms")),
        ExaminationFindings = reader.GetString(reader.GetOrdinal("ExaminationFindings")),
        Notes = reader.GetString(reader.GetOrdinal("Notes")),
        CreatedByDoctorId = reader.IsDBNull(reader.GetOrdinal("CreatedByDoctorId"))
            ? null
            : reader.GetGuid(reader.GetOrdinal("CreatedByDoctorId"))
    };
}
