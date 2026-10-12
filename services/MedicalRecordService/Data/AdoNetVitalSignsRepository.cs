using MedicalRecordService.Models.Entities;
using MedicalRecordService.Models.Enums;
using MySqlConnector;

namespace MedicalRecordService.Data;

public sealed class AdoNetVitalSignsRepository : IVitalSignsRepository
{
    private const int DuplicateKeyErrorNumber = 1062;

    private readonly IMedicalRecordConnectionFactory _connectionFactory;

    public AdoNetVitalSignsRepository(IMedicalRecordConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<VitalSignsPersistenceOutcome> CreateAsync(
        VitalSignsDraft vitalSigns,
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO VitalSigns (
                Id,
                ConsultationId,
                SystolicBloodPressure,
                DiastolicBloodPressure,
                TemperatureCelsius,
                PulseRate,
                RespiratoryRate,
                OxygenSaturation,
                HeightCentimeters,
                WeightKilograms,
                Bmi,
                RecordedAt
            )
            SELECT
                @Id,
                consultation.Id,
                @SystolicBloodPressure,
                @DiastolicBloodPressure,
                @TemperatureCelsius,
                @PulseRate,
                @RespiratoryRate,
                @OxygenSaturation,
                @HeightCentimeters,
                @WeightKilograms,
                @Bmi,
                @RecordedAt
            FROM Consultations AS consultation
            WHERE consultation.Id = @ConsultationId
              AND consultation.DoctorId = @DoctorId;
            """;

        command.Parameters.Add("@Id", MySqlDbType.VarChar, 36).Value = vitalSigns.Id.ToString();
        command.Parameters.Add("@ConsultationId", MySqlDbType.VarChar, 36).Value =
            vitalSigns.ConsultationId.ToString();
        command.Parameters.Add("@DoctorId", MySqlDbType.VarChar, 36).Value = doctorId.ToString();
        AddNullableInt(command, "@SystolicBloodPressure", vitalSigns.SystolicBloodPressure);
        AddNullableInt(command, "@DiastolicBloodPressure", vitalSigns.DiastolicBloodPressure);
        AddNullableDecimal(
            command,
            "@TemperatureCelsius",
            vitalSigns.TemperatureCelsius,
            precision: 4,
            scale: 1);
        AddNullableInt(command, "@PulseRate", vitalSigns.PulseRate);
        AddNullableInt(command, "@RespiratoryRate", vitalSigns.RespiratoryRate);
        AddNullableInt(command, "@OxygenSaturation", vitalSigns.OxygenSaturation);
        AddNullableDecimal(
            command,
            "@HeightCentimeters",
            vitalSigns.HeightCentimeters,
            precision: 5,
            scale: 2);
        AddNullableDecimal(
            command,
            "@WeightKilograms",
            vitalSigns.WeightKilograms,
            precision: 6,
            scale: 2);
        AddNullableDecimal(command, "@Bmi", vitalSigns.Bmi, precision: 5, scale: 2);
        command.Parameters.Add("@RecordedAt", MySqlDbType.DateTime).Value = vitalSigns.RecordedAt;

        try
        {
            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            return rowsAffected == 1
                ? VitalSignsPersistenceOutcome.Success
                : VitalSignsPersistenceOutcome.ConsultationNotFound;
        }
        catch (MySqlException exception) when (exception.Number == DuplicateKeyErrorNumber)
        {
            return VitalSignsPersistenceOutcome.VitalSignsAlreadyExist;
        }
    }

    private static void AddNullableInt(MySqlCommand command, string name, int? value)
    {
        command.Parameters.Add(name, MySqlDbType.Int32).Value = (object?)value ?? DBNull.Value;
    }

    private static void AddNullableDecimal(
        MySqlCommand command,
        string name,
        decimal? value,
        byte precision,
        byte scale)
    {
        var parameter = command.Parameters.Add(name, MySqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = (object?)value ?? DBNull.Value;
    }
}
