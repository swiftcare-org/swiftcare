using MySqlConnector;

namespace E2ETests.Support;

// SWC-40 makes a DISPENSED prescription read-only, but no production API can dispense a
// prescription until SWC-41. This helper therefore marks one prescription created by the
// current test, selected by both prescription and patient id, as DISPENSED.
public static class PrescriptionDatabase
{
    public static void MarkDispensed(string prescriptionId, string patientId)
    {
        using var connection = new MySqlConnection(ConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE Prescriptions " +
            "SET Status = 'DISPENSED', UpdatedAt = UTC_TIMESTAMP(6) " +
            "WHERE Id = @prescriptionId AND PatientId = @patientId AND Status = 'PENDING'";
        command.Parameters.AddWithValue("@prescriptionId", prescriptionId);
        command.Parameters.AddWithValue("@patientId", patientId);

        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException(
                $"Could not mark E2E prescription {prescriptionId} for patient {patientId} as DISPENSED.");
        }
    }

    private static string ConnectionString()
    {
        var explicitConnectionString = Environment.GetEnvironmentVariable("E2E_PRESCRIPTION_DB_CONNECTION");
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return explicitConnectionString;
        }

        var password = Environment.GetEnvironmentVariable("MYSQL_PASSWORD")
            ?? throw new InvalidOperationException(
                "MYSQL_PASSWORD is not set. SWC-40 needs it to mark only the prescription created by " +
                "its own test as DISPENSED. Set MYSQL_PASSWORD from the repo root .env, or set " +
                "E2E_PRESCRIPTION_DB_CONNECTION to a full connection string.");

        var builder = new MySqlConnectionStringBuilder
        {
            Server = Environment.GetEnvironmentVariable("E2E_PRESCRIPTION_DB_HOST") ?? "localhost",
            Port = uint.TryParse(Environment.GetEnvironmentVariable("MYSQL_PORT"), out var port) ? port : 3306,
            UserID = Environment.GetEnvironmentVariable("MYSQL_USER") ?? "swiftcare",
            Password = password,
            Database = Environment.GetEnvironmentVariable("PRESCRIPTION_DB_NAME") ?? "swiftcare_prescription",
        };

        return builder.ConnectionString;
    }
}
