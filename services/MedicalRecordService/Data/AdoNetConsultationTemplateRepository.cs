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

    public async Task<IReadOnlyList<ConsultationTemplate>> ListActiveAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Symptoms, ExaminationFindings, Notes
            FROM ConsultationTemplates
            WHERE IsActive = TRUE
            ORDER BY Name;
            """;

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
        Notes = reader.GetString(reader.GetOrdinal("Notes"))
    };
}
