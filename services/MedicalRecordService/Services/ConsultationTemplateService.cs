using MedicalRecordService.Data;
using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Services;

public sealed class ConsultationTemplateService : IConsultationTemplateService
{
    private readonly IMedicalRecordConnectionFactory _connectionFactory;

    public ConsultationTemplateService(IMedicalRecordConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ConsultationTemplateResponse>> GetActiveTemplatesAsync(
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
        var templates = new List<ConsultationTemplateResponse>();

        var idOrdinal = reader.GetOrdinal("Id");
        var nameOrdinal = reader.GetOrdinal("Name");
        var symptomsOrdinal = reader.GetOrdinal("Symptoms");
        var examinationFindingsOrdinal = reader.GetOrdinal("ExaminationFindings");
        var notesOrdinal = reader.GetOrdinal("Notes");

        while (await reader.ReadAsync(cancellationToken))
        {
            templates.Add(new ConsultationTemplateResponse
            {
                Id = reader.GetGuid(idOrdinal),
                Name = reader.GetString(nameOrdinal),
                Symptoms = reader.GetString(symptomsOrdinal),
                ExaminationFindings = reader.GetString(examinationFindingsOrdinal),
                Notes = reader.GetString(notesOrdinal)
            });
        }

        return templates;
    }
}
