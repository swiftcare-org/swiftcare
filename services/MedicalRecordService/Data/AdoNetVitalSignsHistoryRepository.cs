using MedicalRecordService.Models.Entities;
using MySqlConnector;

namespace MedicalRecordService.Data;

public sealed class AdoNetVitalSignsHistoryRepository : IVitalSignsHistoryRepository
{
    private readonly IMedicalRecordConnectionFactory _connectionFactory;

    public AdoNetVitalSignsHistoryRepository(IMedicalRecordConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<VitalSigns>> ListForPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                vitals.Id,
                vitals.ConsultationId,
                vitals.SystolicBloodPressure,
                vitals.DiastolicBloodPressure,
                vitals.TemperatureCelsius,
                vitals.PulseRate,
                vitals.RespiratoryRate,
                vitals.OxygenSaturation,
                vitals.HeightCentimeters,
                vitals.WeightKilograms,
                vitals.Bmi,
                vitals.RecordedAt
            FROM VitalSigns AS vitals
            INNER JOIN Consultations AS consultation
                ON consultation.Id = vitals.ConsultationId
            WHERE consultation.PatientId = @PatientId
              AND consultation.Status = @CompleteStatus
            ORDER BY vitals.RecordedAt DESC;
            """;
        command.Parameters.Add("@PatientId", MySqlDbType.VarChar, 36).Value = patientId.ToString();
        command.Parameters.Add("@CompleteStatus", MySqlDbType.VarChar, 16).Value =
            Consultation.CompleteStatus;

        var readings = new List<VitalSigns>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            readings.Add(new VitalSigns
            {
                Id = Guid.Parse(reader.GetValue(0).ToString()!),
                ConsultationId = Guid.Parse(reader.GetValue(1).ToString()!),
                SystolicBloodPressure = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                DiastolicBloodPressure = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                TemperatureCelsius = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                PulseRate = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                RespiratoryRate = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                OxygenSaturation = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                HeightCentimeters = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                WeightKilograms = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                Bmi = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                RecordedAt = DateTime.SpecifyKind(reader.GetDateTime(11), DateTimeKind.Utc)
            });
        }

        return readings;
    }
}
