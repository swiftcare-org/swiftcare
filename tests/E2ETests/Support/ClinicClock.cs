namespace E2ETests.Support;

// The clinic-local calendar date the backend validates and partitions against
// (Asia/Colombo, see QueueService's QueueOptions.ClinicTimeZone and PatientService's
// ClinicDateProvider), never the test machine's own local time or UTC. A test run on a
// machine in any other zone still has to reason about the same day the services do.
public static class ClinicClock
{
    private const string ClinicTimeZoneId = "Asia/Colombo";

    public static DateOnly Today =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(ClinicTimeZoneId)).Date);

    public static string TodayIsoDate() => Today.ToString("yyyy-MM-dd");
}
