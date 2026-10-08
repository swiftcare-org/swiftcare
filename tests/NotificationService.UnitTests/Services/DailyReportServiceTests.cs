using Microsoft.Extensions.Options;
using NotificationService.Models.Configuration;
using NotificationService.Models.Dtos;
using NotificationService.Models.Entities;
using NotificationService.Models.Enums;
using NotificationService.Services;

namespace NotificationService.UnitTests.Services;

// SWC-140: the daily report counts one clinic day (Asia/Colombo, UTC+05:30) of events.
public class DailyReportServiceTests
{
    private static readonly DateOnly ReportDate = new(2026, 10, 8);

    // The clinic day 8 October runs from 18:30 UTC on the 7th to 18:30 UTC on the 8th.
    private static readonly DateTime DayStartUtc = new(2026, 10, 7, 18, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime DayEndUtc = new(2026, 10, 8, 18, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Midday = new(2026, 10, 8, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReportCountsPatientsRoomsAndDiagnosesForTheDay()
    {
        await using var database = await TestDatabase.CreateAsync();
        var patients = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();
        await database.SeedAsync(
            // Seven patients: two new, five returning.
            CheckIn(patients[0], isNew: true),
            CheckIn(patients[1], isNew: true),
            CheckIn(patients[2], isNew: false),
            CheckIn(patients[3], isNew: false),
            CheckIn(patients[4], isNew: false),
            CheckIn(patients[5], isNew: false),
            CheckIn(patients[6], isNew: false),
            // Room 1 sees three patients, Room 2 one, Room 3 none.
            Called(patients[0], "1"),
            Called(patients[1], "1"),
            Called(patients[2], "1"),
            Called(patients[3], "2"),
            Completed(patients[0], "Viral URTI"),
            Completed(patients[1], "Viral URTI"),
            Completed(patients[2], "Hypertension"));

        var report = await CreateService(database).GetAsync(ReportDate);

        Assert.Equal(ReportDate, report.Date);
        Assert.Equal(7, report.TotalPatients);
        Assert.Equal(2, report.NewPatients);
        Assert.Equal(5, report.ReturningPatients);
        Assert.Equal(
            [new RoomPatientCount("1", 3), new RoomPatientCount("2", 1), new RoomPatientCount("3", 0)],
            report.PatientsPerRoom);
        Assert.Equal(
            [new DiagnosisCount("Viral URTI", 2), new DiagnosisCount("Hypertension", 1)],
            report.TopDiagnoses);
    }

    [Fact]
    public async Task PatientWhoChecksInTwiceIsCountedOnce()
    {
        await using var database = await TestDatabase.CreateAsync();
        var patient = Guid.NewGuid();
        await database.SeedAsync(
            CheckIn(patient, isNew: false, Midday),
            CheckIn(patient, isNew: false, Midday.AddHours(3)),
            CheckIn(Guid.NewGuid(), isNew: false));

        var report = await CreateService(database).GetAsync(ReportDate);

        Assert.Equal(2, report.TotalPatients);
        Assert.Equal(0, report.NewPatients);
        Assert.Equal(2, report.ReturningPatients);
    }

    // Registered in the morning, back in the afternoon: still one new patient, not one of each.
    [Fact]
    public async Task PatientRegisteredTodayStaysNewWhenTheyCheckInAgain()
    {
        await using var database = await TestDatabase.CreateAsync();
        var patient = Guid.NewGuid();
        await database.SeedAsync(
            CheckIn(patient, isNew: true, Midday),
            CheckIn(patient, isNew: false, Midday.AddHours(3)));

        var report = await CreateService(database).GetAsync(ReportDate);

        Assert.Equal(1, report.TotalPatients);
        Assert.Equal(1, report.NewPatients);
        Assert.Equal(0, report.ReturningPatients);
    }

    [Fact]
    public async Task PatientCalledTwiceToTheSameRoomIsCountedOnceForThatRoom()
    {
        await using var database = await TestDatabase.CreateAsync();
        var patient = Guid.NewGuid();
        await database.SeedAsync(Called(patient, "2"), Called(patient, "2"), Called(Guid.NewGuid(), "2"));

        var report = await CreateService(database).GetAsync(ReportDate);

        Assert.Equal(2, report.PatientsPerRoom.Single(room => room.RoomNumber == "2").Patients);
    }

    [Fact]
    public async Task RoomOutsideTheKnownThreeIsListedAfterThem()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(Called(Guid.NewGuid(), "7"), Called(Guid.NewGuid(), "3"));

        var report = await CreateService(database).GetAsync(ReportDate);

        Assert.Equal(
            [
                new RoomPatientCount("1", 0),
                new RoomPatientCount("2", 0),
                new RoomPatientCount("3", 1),
                new RoomPatientCount("7", 1)
            ],
            report.PatientsPerRoom);
    }

    [Fact]
    public async Task OnlyTheFiveMostCommonDiagnosesAreReturnedHighestFirst()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(
        [
            .. Repeat("Asthma", 2),
            .. Repeat("Viral URTI", 6),
            .. Repeat("Migraine", 1),
            .. Repeat("Hypertension", 5),
            .. Repeat("Gastritis", 4),
            .. Repeat("Dermatitis", 3)
        ]);

        var report = await CreateService(database).GetAsync(ReportDate);

        Assert.Equal(DailyReportService.TopDiagnosesLimit, report.TopDiagnoses.Count);
        Assert.Equal(
            [
                new DiagnosisCount("Viral URTI", 6),
                new DiagnosisCount("Hypertension", 5),
                new DiagnosisCount("Gastritis", 4),
                new DiagnosisCount("Dermatitis", 3),
                new DiagnosisCount("Asthma", 2)
            ],
            report.TopDiagnoses);
    }

    [Fact]
    public void TopDiagnosesLimitIsFive()
    {
        Assert.Equal(5, DailyReportService.TopDiagnosesLimit);
    }

    [Fact]
    public async Task DiagnosesWithEqualCountsAreOrderedByName()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(
        [
            .. Repeat("Migraine", 2),
            .. Repeat("asthma", 2),
            .. Repeat("Gastritis", 2)
        ]);

        var report = await CreateService(database).GetAsync(ReportDate);

        Assert.Equal(
            ["asthma", "Gastritis", "Migraine"],
            report.TopDiagnoses.Select(diagnosis => diagnosis.Diagnosis).ToArray());
    }

    [Fact]
    public async Task SameDiagnosisInDifferentLetterCaseIsCountedTogether()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(
            Completed(Guid.NewGuid(), "viral urti"),
            Completed(Guid.NewGuid(), "Viral URTI"),
            Completed(Guid.NewGuid(), "VIRAL URTI"));

        var report = await CreateService(database).GetAsync(ReportDate);

        // The spelling shown is always the same one, whatever order the events arrived in.
        Assert.Equal([new DiagnosisCount("VIRAL URTI", 3)], report.TopDiagnoses);
    }

    // Consultations completed before the event carried a diagnosis have none to count.
    [Fact]
    public async Task ConsultationWithoutADiagnosisIsLeftOutOfTheDiagnoses()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(Completed(Guid.NewGuid(), null), Completed(Guid.NewGuid(), "Asthma"));

        var report = await CreateService(database).GetAsync(ReportDate);

        Assert.Equal([new DiagnosisCount("Asthma", 1)], report.TopDiagnoses);
    }

    [Fact]
    public async Task EventsJustOutsideTheClinicDayAreLeftOut()
    {
        await using var database = await TestDatabase.CreateAsync();
        var firstOfTheDay = Guid.NewGuid();
        var lastOfTheDay = Guid.NewGuid();
        await database.SeedAsync(
            // 23:59:59 clinic time the day before, and 00:00 the day after.
            CheckIn(Guid.NewGuid(), isNew: true, DayStartUtc.AddSeconds(-1)),
            CheckIn(Guid.NewGuid(), isNew: true, DayEndUtc),
            Called(Guid.NewGuid(), "1", DayStartUtc.AddSeconds(-1)),
            Called(Guid.NewGuid(), "1", DayEndUtc),
            Completed(Guid.NewGuid(), "Outside", DayStartUtc.AddSeconds(-1)),
            Completed(Guid.NewGuid(), "Outside", DayEndUtc),
            // 00:00 and 23:59:59 clinic time on the day itself.
            CheckIn(firstOfTheDay, isNew: false, DayStartUtc),
            CheckIn(lastOfTheDay, isNew: false, DayEndUtc.AddSeconds(-1)));

        var report = await CreateService(database).GetAsync(ReportDate);

        Assert.Equal(2, report.TotalPatients);
        Assert.Equal(0, report.NewPatients);
        Assert.All(report.PatientsPerRoom, room => Assert.Equal(0, room.Patients));
        Assert.Empty(report.TopDiagnoses);
    }

    // 18:45 UTC on the 7th is 00:15 on the 8th at the clinic.
    [Fact]
    public async Task EventLateOnThePreviousUtcDayBelongsToTheClinicDay()
    {
        await using var database = await TestDatabase.CreateAsync();
        var afterClinicMidnight = new DateTime(2026, 10, 7, 18, 45, 0, DateTimeKind.Utc);
        await database.SeedAsync(CheckIn(Guid.NewGuid(), isNew: true, afterClinicMidnight));
        var service = CreateService(database);

        Assert.Equal(1, (await service.GetAsync(ReportDate)).TotalPatients);
        Assert.Equal(0, (await service.GetAsync(ReportDate.AddDays(-1))).TotalPatients);
    }

    [Fact]
    public async Task DateWithNoActivityGivesZeroTotalsThreeEmptyRoomsAndNoDiagnoses()
    {
        await using var database = await TestDatabase.CreateAsync();

        var report = await CreateService(database).GetAsync(ReportDate);

        Assert.Equal(ReportDate, report.Date);
        Assert.Equal(0, report.TotalPatients);
        Assert.Equal(0, report.NewPatients);
        Assert.Equal(0, report.ReturningPatients);
        Assert.Equal(
            [new RoomPatientCount("1", 0), new RoomPatientCount("2", 0), new RoomPatientCount("3", 0)],
            report.PatientsPerRoom);
        Assert.Empty(report.TopDiagnoses);
    }

    [Fact]
    public async Task ClinicTimeZoneAndRoomsComeFromConfiguration()
    {
        await using var database = await TestDatabase.CreateAsync();
        // 23:00 UTC on the 8th is still the 8th in UTC, but already the 9th at the clinic.
        await database.SeedAsync(CheckIn(Guid.NewGuid(), isNew: true, new DateTime(2026, 10, 8, 23, 0, 0, DateTimeKind.Utc)));
        var service = new DailyReportService(
            database.DbContext,
            Options.Create(new ReportOptions { ClinicTimeZone = "UTC", Rooms = ["A"] }));

        var report = await service.GetAsync(ReportDate);

        Assert.Equal(1, report.TotalPatients);
        Assert.Equal([new RoomPatientCount("A", 0)], report.PatientsPerRoom);
    }

    [Fact]
    public void DefaultsAreTheColomboClinicAndItsThreeRooms()
    {
        var options = new ReportOptions();

        Assert.Equal("Asia/Colombo", options.ClinicTimeZone);
        Assert.Equal(["1", "2", "3"], options.Rooms);
    }

    [Fact]
    public async Task ReadingTheReportTracksAndChangesNothing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(CheckIn(Guid.NewGuid(), isNew: true));

        await CreateService(database).GetAsync(ReportDate);

        Assert.Empty(database.DbContext.ChangeTracker.Entries());
    }

    private static DailyReportService CreateService(TestDatabase database) =>
        new(database.DbContext, Options.Create(new ReportOptions()));

    private static Notification CheckIn(Guid patientId, bool isNew, DateTime? occurredAt = null) =>
        NewEvent(NotificationType.PatientCheckedIn, patientId, occurredAt, isNew: isNew);

    private static Notification Called(Guid patientId, string room, DateTime? occurredAt = null) =>
        NewEvent(NotificationType.PatientCalled, patientId, occurredAt, room: room);

    private static Notification Completed(Guid patientId, string? diagnosis, DateTime? occurredAt = null) =>
        NewEvent(NotificationType.ConsultationCompleted, patientId, occurredAt, diagnosis: diagnosis);

    private static Notification[] Repeat(string diagnosis, int times) =>
        Enumerable.Range(0, times).Select(_ => Completed(Guid.NewGuid(), diagnosis)).ToArray();

    private static Notification NewEvent(
        NotificationType type,
        Guid patientId,
        DateTime? occurredAt,
        bool? isNew = null,
        string? room = null,
        string? diagnosis = null) => new()
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            Type = type,
            PatientId = patientId,
            IsNewPatient = isNew,
            RoomNumber = room,
            Diagnosis = diagnosis,
            OccurredAt = occurredAt ?? Midday,
            ReceivedAt = occurredAt ?? Midday
        };
}
