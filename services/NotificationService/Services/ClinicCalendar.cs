namespace NotificationService.Services;

// Events are stored in UTC, but a report covers clinic days, which start and end at
// midnight in the clinic time zone. This converts between the two.
public sealed class ClinicCalendar(TimeZoneInfo clinicTimeZone)
{
    public ClinicCalendar(string clinicTimeZoneId)
        : this(TimeZoneInfo.FindSystemTimeZoneById(clinicTimeZoneId))
    {
    }

    // The UTC instant at which the given clinic day begins.
    public DateTime StartOfDayUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), clinicTimeZone);

    // The clinic day a stored UTC time falls on.
    public DateOnly DateOf(DateTime utc) =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), clinicTimeZone));
}
