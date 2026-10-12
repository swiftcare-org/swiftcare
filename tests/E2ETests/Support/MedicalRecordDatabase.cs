using MySqlConnector;

namespace E2ETests.Support;

// SWC-122 correctly prevents a doctor from creating a consultation whose follow-up
// date is already in the past. SWC-28 still needs a historical completed consultation
// to exercise the overdue banner, and no production API exists for advancing time.
// This helper therefore changes one consultation created by the current test, selected
// by both consultation and patient id, after the real API has completed it.
public static class MedicalRecordDatabase
{
    public static void BackdateCompletedFollowUp(
        string consultationId,
        string patientId,
        DateOnly followUpDate)
    {
        using var connection = new MySqlConnection(ConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE Consultations " +
            "SET FollowUpDate = @followUpDate " +
            "WHERE Id = @consultationId AND PatientId = @patientId AND Status = 'COMPLETE'";
        command.Parameters.AddWithValue("@followUpDate", followUpDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("@consultationId", consultationId);
        command.Parameters.AddWithValue("@patientId", patientId);

        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException(
                $"Could not backdate completed E2E consultation {consultationId} for patient {patientId}.");
        }
    }

    private static string ConnectionString()
    {
        var explicitConnectionString = Environment.GetEnvironmentVariable("E2E_MEDICAL_RECORD_DB_CONNECTION");
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return explicitConnectionString;
        }

        var password = Environment.GetEnvironmentVariable("MYSQL_PASSWORD")
            ?? throw new InvalidOperationException(
                "MYSQL_PASSWORD is not set. SWC-28 needs it to backdate only the completed consultation " +
                "created by its own test. Set MYSQL_PASSWORD from the repo root .env, or set " +
                "E2E_MEDICAL_RECORD_DB_CONNECTION to a full connection string.");

        var builder = new MySqlConnectionStringBuilder
        {
            Server = Environment.GetEnvironmentVariable("E2E_MEDICAL_RECORD_DB_HOST") ?? "localhost",
            Port = uint.TryParse(Environment.GetEnvironmentVariable("MYSQL_PORT"), out var port) ? port : 3306,
            UserID = Environment.GetEnvironmentVariable("MYSQL_USER") ?? "swiftcare",
            Password = password,
            Database = Environment.GetEnvironmentVariable("MEDICAL_RECORD_DB_NAME") ?? "swiftcare_medical_record",
        };

        return builder.ConnectionString;
    }
}
