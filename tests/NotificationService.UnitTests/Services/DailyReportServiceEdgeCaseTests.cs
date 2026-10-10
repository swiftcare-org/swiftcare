using Microsoft.Extensions.Options;
using NotificationService.Models.Configuration;
using NotificationService.Models.Dtos;
using NotificationService.Models.Entities;
using NotificationService.Models.Enums;
using NotificationService.Services;

namespace NotificationService.UnitTests.Services;

// Which events count towards a room, and the order of rooms outside the known three
// (SWC-151 mutation testing).
public class DailyReportServiceEdgeCaseTests
{
    private static readonly DateOnly ReportDate = new(2026, 10, 8);
    private static readonly DateTime Midday = new(2026, 10, 8, 6, 0, 0, DateTimeKind.Utc);

    // Only a call puts a patient in a room. Another event that carries a room number must
    // not be counted for it.
    [Fact]
    public async Task OnlyCallEventsCountTowardsARoom()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(
            NewEvent(NotificationType.PatientCalled, room: "1"),
            NewEvent(NotificationType.PatientCheckedIn, room: "1"),
            NewEvent(NotificationType.ConsultationCompleted, room: "2"));

        var report = await CreateService(database).GetAsync(ReportDate);

        Assert.Equal(
            [new RoomPatientCount("1", 1), new RoomPatientCount("2", 0), new RoomPatientCount("3", 0)],
            report.PatientsPerRoom);
    }

    [Fact]
    public async Task RoomsOutsideTheKnownThreeAreListedByNameIgnoringCase()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync(
            NewEvent(NotificationType.PatientCalled, room: "Lab"),
            NewEvent(NotificationType.PatientCalled, room: "7"),
            NewEvent(NotificationType.PatientCalled, room: "annex"));

        var report = await CreateService(database).GetAsync(ReportDate);

        Assert.Equal(
            ["1", "2", "3", "7", "annex", "Lab"],
            report.PatientsPerRoom.Select(room => room.RoomNumber));
    }

    private static DailyReportService CreateService(TestDatabase database) =>
        new(database.DbContext, Options.Create(new ReportOptions()));

    private static Notification NewEvent(NotificationType type, string room) => new()
    {
        Id = Guid.NewGuid(),
        EventId = Guid.NewGuid(),
        Type = type,
        PatientId = Guid.NewGuid(),
        RoomNumber = room,
        OccurredAt = Midday,
        ReceivedAt = Midday
    };
}
