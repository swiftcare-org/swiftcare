using MedicalRecordService.Models.Entities;
using MySqlConnector;

namespace MedicalRecordService.Data;

public sealed class AdoNetConsultationHistoryRepository : IConsultationHistoryRepository
{
    private const string SelectCompletedForPatient = """
        SELECT
            Id,
            PatientId,
            QueueId,
            DoctorId,
            DoctorName,
            RoomNumber,
            Symptoms,
            ExaminationFindings,
            Diagnosis,
            Notes,
            FollowUpDate,
            FollowUpInstructions,
            TemplateId,
            TemplateName,
            ConsultationDate,
            CreatedAt
        FROM Consultations
        WHERE PatientId = @PatientId AND Status = @CompleteStatus
        ORDER BY ConsultationDate DESC
        """;

    private readonly IMedicalRecordConnectionFactory _connectionFactory;

    public AdoNetConsultationHistoryRepository(IMedicalRecordConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Consultation>> ListCompletedAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        return await QueryAsync($"{SelectCompletedForPatient};", patientId, cancellationToken);
    }

    public async Task<Consultation?> FindLatestCompletedAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var consultations = await QueryAsync(
            $"{SelectCompletedForPatient} LIMIT 1;",
            patientId,
            cancellationToken);

        return consultations.Count == 0 ? null : consultations[0];
    }

    private async Task<IReadOnlyList<Consultation>> QueryAsync(
        string commandText,
        Guid patientId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.Add("@PatientId", MySqlDbType.VarChar, 36).Value = patientId.ToString();
        command.Parameters.Add("@CompleteStatus", MySqlDbType.VarChar, 16).Value =
            Consultation.CompleteStatus;

        var consultations = new List<Consultation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            consultations.Add(new Consultation
            {
                Id = Guid.Parse(reader.GetValue(0).ToString()!),
                PatientId = Guid.Parse(reader.GetValue(1).ToString()!),
                QueueId = Guid.Parse(reader.GetValue(2).ToString()!),
                DoctorId = Guid.Parse(reader.GetValue(3).ToString()!),
                DoctorName = reader.GetString(4),
                RoomNumber = reader.GetString(5),
                Symptoms = reader.GetString(6),
                ExaminationFindings = reader.IsDBNull(7) ? null : reader.GetString(7),
                Diagnosis = reader.GetString(8),
                Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
                FollowUpDate = reader.IsDBNull(10)
                    ? null
                    : DateOnly.FromDateTime(reader.GetDateTime(10)),
                FollowUpInstructions = reader.IsDBNull(11) ? null : reader.GetString(11),
                TemplateId = reader.IsDBNull(12)
                    ? null
                    : Guid.Parse(reader.GetValue(12).ToString()!),
                TemplateName = reader.IsDBNull(13) ? null : reader.GetString(13),
                ConsultationDate = DateTime.SpecifyKind(reader.GetDateTime(14), DateTimeKind.Utc),
                CreatedAt = DateTime.SpecifyKind(reader.GetDateTime(15), DateTimeKind.Utc),
                Status = Consultation.CompleteStatus
            });
        }

        return consultations;
    }
}
