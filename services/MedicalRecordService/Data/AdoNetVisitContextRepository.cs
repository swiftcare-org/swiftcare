using MedicalRecordService.Models.Dtos;
using MySqlConnector;

namespace MedicalRecordService.Data;

public sealed class AdoNetVisitContextRepository(IMedicalRecordConnectionFactory connections) : IVisitContextRepository
{
    public async Task<VisitContextResponse?> FindAsync(Guid consultationId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PatientId, QueueId, DoctorId, Status FROM Consultations WHERE Id = @Id";
        command.Parameters.Add("@Id", MySqlDbType.VarChar, 36).Value = consultationId.ToString();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(Guid.Parse(reader.GetValue(0).ToString()!), Guid.Parse(reader.GetValue(1).ToString()!),
            Guid.Parse(reader.GetValue(2).ToString()!), reader.GetString(3));
    }
}
