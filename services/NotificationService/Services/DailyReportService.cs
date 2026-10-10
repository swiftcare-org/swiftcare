using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationService.Data;
using NotificationService.Models.Configuration;
using NotificationService.Models.Dtos;
using NotificationService.Models.Enums;

namespace NotificationService.Services;

// Counts one clinic day of stored events. The day's events are read once and counted in
// memory: a day holds a few hundred at most, and counting here gives the same result on
// every database.
public sealed class DailyReportService : IDailyReportService
{
    public const int TopDiagnosesLimit = 5;

    private readonly NotificationDbContext _dbContext;
    private readonly ClinicCalendar _calendar;
    private readonly IReadOnlyList<string> _rooms;

    public DailyReportService(NotificationDbContext dbContext, IOptions<ReportOptions> options)
    {
        _dbContext = dbContext;
        _calendar = new ClinicCalendar(options.Value.ClinicTimeZone);
        _rooms = options.Value.Rooms;
    }

    public async Task<DailyReportResponse> GetAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        // Events are stored in UTC, so the clinic day is turned into the UTC span it covers.
        var fromUtc = _calendar.StartOfDayUtc(date);
        var toUtc = _calendar.StartOfDayUtc(date.AddDays(1));

        var events = await _dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.OccurredAt >= fromUtc && notification.OccurredAt < toUtc)
            .Select(notification => new DayEvent(
                notification.Type,
                notification.PatientId,
                notification.IsNewPatient,
                notification.RoomNumber,
                notification.Diagnosis))
            .ToListAsync(cancellationToken);

        var checkIns = events.Where(item => item.Type == NotificationType.PatientCheckedIn).ToList();
        var totalPatients = checkIns.Select(item => item.PatientId).Distinct().Count();
        // A patient registered today is new for the whole day, even if they check in again.
        var newPatients = checkIns
            .Where(item => item.IsNewPatient == true)
            .Select(item => item.PatientId)
            .Distinct()
            .Count();

        return new DailyReportResponse(
            date,
            totalPatients,
            newPatients,
            totalPatients - newPatients,
            CountPatientsPerRoom(events),
            DiagnosisRanking.Top(
                events
                    .Where(item => item.Type == NotificationType.ConsultationCompleted)
                    .Select(item => item.Diagnosis),
                TopDiagnosesLimit));
    }

    // A patient called to the same room twice is one patient for that room.
    private List<RoomPatientCount> CountPatientsPerRoom(IEnumerable<DayEvent> events)
    {
        var patientsByRoom = events
            .Where(item => item.Type == NotificationType.PatientCalled && item.RoomNumber is not null)
            .GroupBy(item => item.RoomNumber!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                room => room.Key,
                room => room.Select(item => item.PatientId).Distinct().Count(),
                StringComparer.OrdinalIgnoreCase);

        // The known rooms come first and are always listed; any other room follows by name.
        var otherRooms = patientsByRoom.Keys
            .Where(room => !_rooms.Contains(room, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);

        return _rooms
            .Concat(otherRooms)
            .Select(room => new RoomPatientCount(room, patientsByRoom.GetValueOrDefault(room)))
            .ToList();
    }

    private sealed record DayEvent(
        NotificationType Type,
        Guid PatientId,
        bool? IsNewPatient,
        string? RoomNumber,
        string? Diagnosis);
}
