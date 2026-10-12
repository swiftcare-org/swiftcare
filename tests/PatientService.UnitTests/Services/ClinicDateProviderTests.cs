using Microsoft.Extensions.Options;
using PatientService.Models.Configuration;
using PatientService.Services;

namespace PatientService.UnitTests.Services;

public sealed class ClinicDateProviderTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Theory]
    [InlineData("2026-09-09T18:29:59Z", 2026, 9, 9)]
    [InlineData("2026-09-09T18:30:00Z", 2026, 9, 10)]
    [InlineData("2026-09-09T20:30:00Z", 2026, 9, 10)]
    public void TodayUsesTheAsiaColomboCalendarDate(
        string utcTimestamp,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        var provider = new ClinicDateProvider(
            new FixedTimeProvider(DateTimeOffset.Parse(utcTimestamp)),
            Options.Create(new ClinicOptions { TimeZoneId = "Asia/Colombo" }));

        Assert.Equal(
            new DateOnly(expectedYear, expectedMonth, expectedDay),
            provider.Today);
    }
}
