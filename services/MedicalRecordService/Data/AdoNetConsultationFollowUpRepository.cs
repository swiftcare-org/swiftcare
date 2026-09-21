using MedicalRecordService.Models.Entities;
using MySqlConnector;

namespace MedicalRecordService.Data;

public sealed class AdoNetConsultationFollowUpRepository : IConsultationFollowUpRepository
{
    private readonly IMedicalRecordConnectionFactory _connectionFactory;

    public AdoNetConsultationFollowUpRepository(
        IMedicalRecordConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ConsultationFollowUp?> FindLatestCompletedAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, FollowUpDate, FollowUpInstructions
            FROM Consultations
            WHERE PatientId = @PatientId AND Status = @CompleteStatus
            ORDER BY ConsultationDate DESC
            LIMIT 1;
            """;
        command.Parameters.Add("@PatientId", MySqlDbType.VarChar, 36).Value = patientId.ToString();
        command.Parameters.Add("@CompleteStatus", MySqlDbType.VarChar, 16).Value =
            Consultation.CompleteStatus;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ConsultationFollowUp(
            Guid.Parse(reader.GetValue(0).ToString()!),
            reader.IsDBNull(1) ? null : DateOnly.FromDateTime(reader.GetDateTime(1)),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }
}
