using MySqlConnector;

namespace E2ETests.Support;

// The one place this suite reaches past HTTP and touches a service's database directly.
//
// SWC-15's happy path needs a patient who exists but is NOT in today's queue, and that
// state is unreachable through the API: registering a patient publishes patient-checked-in
// with IsNewPatient true, QueueService queues them immediately, and no shipped endpoint
// removes or completes a queue entry (QueueController exposes display, today,
// today/waiting, call-next and today/patient/{id} only). QA reached the same precondition
// by hand with a direct DELETE - see docs/testing/SWC-15-test-results.md, TC-02.
//
// Scope is deliberately narrow: one row, identified by patient id and today's clinic date,
// for a patient this run registered itself. Nothing here ever touches another test's data.
// SWC-111 also uses this helper to remove registration's incidental queue side effect from
// profile/search tests before those tests join the bounded parallel worker pool.
public static class QueueDatabase
{
    // Deletes today's queue entry for one patient and reports whether a row was actually
    // removed, so a caller can fail loudly rather than silently testing the wrong state.
    public static bool DeleteTodayQueueEntry(string patientId)
    {
        using var connection = new MySqlConnection(ConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM QueueEntries WHERE PatientId = @patientId AND QueueDate = @queueDate";
        command.Parameters.AddWithValue("@patientId", patientId);
        command.Parameters.AddWithValue("@queueDate", ClinicClock.TodayIsoDate());

        return command.ExecuteNonQuery() > 0;
    }

    // Built from the same .env values docker-compose publishes MySQL with, in the same
    // fail-fast style as TestConfig.SeedPassword: a missing password is a misconfigured
    // environment and should say so immediately rather than surface as a confusing
    // mid-test connection error. Host defaults to localhost rather than reading MYSQL_HOST,
    // because MYSQL_HOST is the in-network container name the services use and is not
    // resolvable from the machine running these tests.
    private static string ConnectionString()
    {
        var explicitConnectionString = Environment.GetEnvironmentVariable("E2E_QUEUE_DB_CONNECTION");
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return explicitConnectionString;
        }

        var password = Environment.GetEnvironmentVariable("MYSQL_PASSWORD")
            ?? throw new InvalidOperationException(
                "MYSQL_PASSWORD is not set. The SWC-15 check-in tests need it to reach the precondition " +
                "'patient exists but is not in today's queue', which no API can produce. Set MYSQL_PASSWORD " +
                "(and optionally MYSQL_USER, MYSQL_PORT, QUEUE_DB_NAME) from the repo root .env, or set " +
                "E2E_QUEUE_DB_CONNECTION to a full connection string. See tests/E2ETests/README.md.");

        var builder = new MySqlConnectionStringBuilder
        {
            Server = Environment.GetEnvironmentVariable("E2E_QUEUE_DB_HOST") ?? "localhost",
            Port = uint.TryParse(Environment.GetEnvironmentVariable("MYSQL_PORT"), out var port) ? port : 3306,
            UserID = Environment.GetEnvironmentVariable("MYSQL_USER") ?? "swiftcare",
            Password = password,
            Database = Environment.GetEnvironmentVariable("QUEUE_DB_NAME") ?? "swiftcare_queue",
        };

        return builder.ConnectionString;
    }
}
