using Microsoft.Extensions.Options;
using NotificationService.Models.Configuration;
using NotificationService.Models.Dtos;
using NotificationService.Models.Entities;
using NotificationService.Models.Enums;
using NotificationService.Services;

namespace NotificationService.UnitTests.Services;

// SWC-145: the monthly report counts one calendar month of clinic days (Asia/Colombo,
// UTC+05:30) and splits its patients into four weeks.
public class MonthlyReportServiceTests
{
    private static readonly DateOnly October = new(2026, 10, 1);

    // October at the clinic runs from 18:30 UTC on 30 September to 18:30 UTC on 31 October.
    private static readonly DateTime MonthStartUtc = new(2026, 9, 30, 18, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime MonthEndUtc = new(2026, 10, 31, 18, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReportCountsPatientsDiagnosesAndWeeksForTheMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(
        [
            // Ten patients: 4 in week 1, 3 in week 2, 2 in week 3, 1 in week 4. Three are new.
            CheckIn(Guid.NewGuid(), isNew: true, Day(1)),
            CheckIn(Guid.NewGuid(), isNew: true, Day(3)),
            CheckIn(Guid.NewGuid(), isNew: false, Day(5)),
            CheckIn(Guid.NewGuid(), isNew: false, Day(7)),
            CheckIn(Guid.NewGuid(), isNew: true, Day(8)),
            CheckIn(Guid.NewGuid(), isNew: false, Day(10)),
            CheckIn(Guid.NewGuid(), isNew: false, Day(14)),
            CheckIn(Guid.NewGuid(), isNew: false, Day(15)),
            CheckIn(Guid.NewGuid(), isNew: false, Day(21)),
            CheckIn(Guid.NewGuid(), isNew: false, Day(22)),
            .. Repeat("Viral URTI", 3, Day(4)),
            .. Repeat("Hypertension", 2, Day(12)),
            .. Repeat("Asthma", 1, Day(25))
        ]);

        var report = await CreateService(database).GetAsync(October);

        Assert.Equal("2026-10", report.Month);
        Assert.Equal(10, report.TotalPatients);
        Assert.Equal(3, report.NewPatients);
        Assert.Equal(7, report.ReturningPatients);
        Assert.Equal(
            [
                new DiagnosisCount("Viral URTI", 3),
                new DiagnosisCount("Hypertension", 2),
                new DiagnosisCount("Asthma", 1)
            ],
            report.TopDiagnoses);
        Assert.Equal(
            [
                new WeekPatientCount(1, 4),
                new WeekPatientCount(2, 3),
                new WeekPatientCount(3, 2),
                new WeekPatientCount(4, 1)
            ],
            report.WeeklyBreakdown);
    }

    [Fact]
    public async Task PatientSeenSeveralTimesInTheMonthIsCountedOnce()
    {
        await using var database = await TestDatabase.CreateAsync();
        var patient = Guid.NewGuid();
        await database.SeedAsync(
            CheckIn(patient, isNew: false, Day(2)),
            CheckIn(patient, isNew: false, Day(9)),
            CheckIn(patient, isNew: false, Day(27)),
            CheckIn(Guid.NewGuid(), isNew: false, Day(27)));

        var report = await CreateService(database).GetAsync(October);

        Assert.Equal(2, report.TotalPatients);
        Assert.Equal(0, report.NewPatients);
        Assert.Equal(2, report.ReturningPatients);
    }

    // The week is the one the patient first came in, whatever order the events were stored in.
    [Fact]
    public async Task PatientIsCountedInTheWeekOfTheirFirstVisitSoWeeksAddUpToTheTotal()
    {
        await using var database = await TestDatabase.CreateAsync();
        var patient = Guid.NewGuid();
        await database.SeedAsync(
            CheckIn(patient, isNew: false, Day(23)),
            CheckIn(patient, isNew: false, Day(9)),
            CheckIn(patient, isNew: false, Day(16)));

        var report = await CreateService(database).GetAsync(October);

        Assert.Equal(
            [
                new WeekPatientCount(1, 0),
                new WeekPatientCount(2, 1),
                new WeekPatientCount(3, 0),
                new WeekPatientCount(4, 0)
            ],
            report.WeeklyBreakdown);
        Assert.Equal(report.TotalPatients, report.WeeklyBreakdown.Sum(week => week.Patients));
    }

    // Registered early in the month and back later: one new patient, not one of each.
    [Fact]
    public async Task PatientRegisteredThisMonthStaysNewWhenTheyReturn()
    {
        await using var database = await TestDatabase.CreateAsync();
        var patient = Guid.NewGuid();
        await database.SeedAsync(
            CheckIn(patient, isNew: true, Day(2)),
            CheckIn(patient, isNew: false, Day(20)));

        var report = await CreateService(database).GetAsync(October);

        Assert.Equal(1, report.TotalPatients);
        Assert.Equal(1, report.NewPatients);
        Assert.Equal(0, report.ReturningPatients);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    [InlineData(14, 2)]
    [InlineData(15, 3)]
    [InlineData(21, 3)]
    [InlineData(22, 4)]
    [InlineData(28, 4)]
    [InlineData(29, 4)]
    [InlineData(30, 4)]
    [InlineData(31, 4)]
    public void EachDayOfTheMonthFallsInTheRightWeek(int dayOfMonth, int expectedWeek)
    {
        Assert.Equal(expectedWeek, MonthlyReportService.WeekOfMonth(dayOfMonth));
    }

    [Theory]
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    [InlineData(14, 2)]
    [InlineData(15, 3)]
    [InlineData(21, 3)]
    [InlineData(22, 4)]
    [InlineData(29, 4)]
    [InlineData(30, 4)]
    [InlineData(31, 4)]
    public async Task PatientSeenOnABoundaryDayIsCountedInThatWeek(int dayOfMonth, int expectedWeek)
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(CheckIn(Guid.NewGuid(), isNew: false, Day(dayOfMonth)));

        var report = await CreateService(database).GetAsync(October);

        Assert.Equal(1, report.WeeklyBreakdown.Single(week => week.Week == expectedWeek).Patients);
        Assert.Equal(1, report.WeeklyBreakdown.Sum(week => week.Patients));
    }

    // The week comes from the clinic day: 18:45 UTC on the 7th is already the 8th at the clinic.
    [Fact]
    public async Task WeekIsTakenFromTheClinicDayNotTheUtcDay()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(
            CheckIn(Guid.NewGuid(), isNew: false, new DateTime(2026, 10, 7, 18, 45, 0, DateTimeKind.Utc)));

        var report = await CreateService(database).GetAsync(October);

        Assert.Equal(0, report.WeeklyBreakdown[0].Patients);
        Assert.Equal(1, report.WeeklyBreakdown[1].Patients);
    }

    [Theory]
    // A 30-day month, a normal February and a leap-year February.
    [InlineData(2026, 11, 30)]
    [InlineData(2026, 2, 28)]
    [InlineData(2028, 2, 29)]
    public async Task LastDayOfAShorterMonthIsCountedInWeekFourAndTheNextDayIsLeftOut(int year, int month, int lastDay)
    {
        await using var database = await TestDatabase.CreateAsync();
        var onLastDay = new DateTime(year, month, lastDay, 6, 0, 0, DateTimeKind.Utc);
        await database.SeedAsync(
            CheckIn(Guid.NewGuid(), isNew: false, onLastDay),
            CheckIn(Guid.NewGuid(), isNew: false, onLastDay.AddDays(1)));

        var report = await CreateService(database).GetAsync(new DateOnly(year, month, 1));

        Assert.Equal(1, report.TotalPatients);
        Assert.Equal(1, report.WeeklyBreakdown[3].Patients);
    }

    [Fact]
    public async Task EventsJustOutsideTheClinicMonthAreLeftOut()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(
            // 23:59:59 on 30 September and 00:00 on 1 November, clinic time.
            CheckIn(Guid.NewGuid(), isNew: true, MonthStartUtc.AddSeconds(-1)),
            CheckIn(Guid.NewGuid(), isNew: true, MonthEndUtc),
            Completed("Outside", MonthStartUtc.AddSeconds(-1)),
            Completed("Outside", MonthEndUtc),
            // 00:00 on 1 October and 23:59:59 on 31 October, clinic time.
            CheckIn(Guid.NewGuid(), isNew: false, MonthStartUtc),
            CheckIn(Guid.NewGuid(), isNew: false, MonthEndUtc.AddSeconds(-1)),
            Completed("Inside", MonthStartUtc));

        var report = await CreateService(database).GetAsync(October);

        Assert.Equal(2, report.TotalPatients);
        Assert.Equal(0, report.NewPatients);
        Assert.Equal([new DiagnosisCount("Inside", 1)], report.TopDiagnoses);
        Assert.Equal(1, report.WeeklyBreakdown[0].Patients);
        Assert.Equal(1, report.WeeklyBreakdown[3].Patients);
    }

    [Fact]
    public async Task AnyDayOfTheMonthSelectsTheWholeMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(CheckIn(Guid.NewGuid(), isNew: false, Day(2)));

        var report = await CreateService(database).GetAsync(new DateOnly(2026, 10, 19));

        Assert.Equal("2026-10", report.Month);
        Assert.Equal(1, report.TotalPatients);
    }

    [Fact]
    public async Task OnlyTheFiveMostCommonDiagnosesAreReturnedWithEqualCountsByName()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(
        [
            .. Repeat("Migraine", 1, Day(3)),
            .. Repeat("Viral URTI", 4, Day(3)),
            .. Repeat("Gastritis", 2, Day(3)),
            .. Repeat("asthma", 2, Day(3)),
            .. Repeat("Hypertension", 3, Day(3)),
            .. Repeat("Dermatitis", 2, Day(3))
        ]);

        var report = await CreateService(database).GetAsync(October);

        Assert.Equal(
            [
                new DiagnosisCount("Viral URTI", 4),
                new DiagnosisCount("Hypertension", 3),
                new DiagnosisCount("asthma", 2),
                new DiagnosisCount("Dermatitis", 2),
                new DiagnosisCount("Gastritis", 2)
            ],
            report.TopDiagnoses);
    }

    [Fact]
    public void ReportHasFiveDiagnosesAndFourWeeks()
    {
        Assert.Equal(5, MonthlyReportService.TopDiagnosesLimit);
        Assert.Equal(4, MonthlyReportService.WeeksInReport);
        Assert.Equal("yyyy-MM", MonthlyReportService.MonthFormat);
    }

    // A patient called to a room is not a check-in and has no diagnosis: it changes nothing here.
    [Fact]
    public async Task PatientCalledEventsAreNotCounted()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(NewEvent(NotificationType.PatientCalled, Guid.NewGuid(), Day(5)));

        var report = await CreateService(database).GetAsync(October);

        Assert.Equal(0, report.TotalPatients);
        Assert.Empty(report.TopDiagnoses);
    }

    [Fact]
    public async Task MonthWithNoActivityGivesZeroTotalsNoDiagnosesAndFourEmptyWeeks()
    {
        await using var database = await TestDatabase.CreateAsync();

        var report = await CreateService(database).GetAsync(October);

        Assert.Equal("2026-10", report.Month);
        Assert.Equal(0, report.TotalPatients);
        Assert.Equal(0, report.NewPatients);
        Assert.Equal(0, report.ReturningPatients);
        Assert.Empty(report.TopDiagnoses);
        Assert.Equal(
            [
                new WeekPatientCount(1, 0),
                new WeekPatientCount(2, 0),
                new WeekPatientCount(3, 0),
                new WeekPatientCount(4, 0)
            ],
            report.WeeklyBreakdown);
    }

    [Fact]
    public async Task ClinicTimeZoneComesFromConfiguration()
    {
        await using var database = await TestDatabase.CreateAsync();
        // 23:00 UTC on 31 October is still October in UTC, but already November at the clinic.
        await database.SeedAsync(
            CheckIn(Guid.NewGuid(), isNew: false, new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc)));
        var utcService = new MonthlyReportService(
            database.DbContext, Options.Create(new ReportOptions { ClinicTimeZone = "UTC" }));

        Assert.Equal(1, (await utcService.GetAsync(October)).TotalPatients);
        Assert.Equal(0, (await CreateService(database).GetAsync(October)).TotalPatients);
    }

    [Fact]
    public async Task ReadingTheReportTracksAndChangesNothing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(CheckIn(Guid.NewGuid(), isNew: true, Day(1)));

        await CreateService(database).GetAsync(October);

        Assert.Empty(database.DbContext.ChangeTracker.Entries());
    }

    private static MonthlyReportService CreateService(TestDatabase database) =>
        new(database.DbContext, Options.Create(new ReportOptions()));

    // 06:00 UTC is 11:30 at the clinic, so the clinic day is the same as the UTC day.
    private static DateTime Day(int dayOfOctober) => new(2026, 10, dayOfOctober, 6, 0, 0, DateTimeKind.Utc);

    private static Notification CheckIn(Guid patientId, bool isNew, DateTime occurredAt) =>
        NewEvent(NotificationType.PatientCheckedIn, patientId, occurredAt, isNew: isNew);

    private static Notification Completed(string? diagnosis, DateTime occurredAt) =>
        NewEvent(NotificationType.ConsultationCompleted, Guid.NewGuid(), occurredAt, diagnosis: diagnosis);

    private static Notification[] Repeat(string diagnosis, int times, DateTime occurredAt) =>
        Enumerable.Range(0, times).Select(_ => Completed(diagnosis, occurredAt)).ToArray();

    private static Notification NewEvent(
        NotificationType type,
        Guid patientId,
        DateTime occurredAt,
        bool? isNew = null,
        string? diagnosis = null) => new()
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            Type = type,
            PatientId = patientId,
            IsNewPatient = isNew,
            RoomNumber = type == NotificationType.PatientCalled ? "1" : null,
            Diagnosis = diagnosis,
            OccurredAt = occurredAt,
            ReceivedAt = occurredAt
        };
}
