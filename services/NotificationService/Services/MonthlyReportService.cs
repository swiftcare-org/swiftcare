using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationService.Data;
using NotificationService.Models.Configuration;
using NotificationService.Models.Dtos;
using NotificationService.Models.Enums;

namespace NotificationService.Services;

// Counts one calendar month of clinic days. Like the daily report, the events of the month
// are read once and counted in memory, which gives the same result on every database.
public sealed class MonthlyReportService : IMonthlyReportService
{
    public const string MonthFormat = "yyyy-MM";
    public const int TopDiagnosesLimit = 5;
    public const int WeeksInReport = 4;

    private const int DaysInWeek = 7;

    private readonly NotificationDbContext _dbContext;
    private readonly ClinicCalendar _calendar;

    public MonthlyReportService(NotificationDbContext dbContext, IOptions<ReportOptions> options)
    {
        _dbContext = dbContext;
        _calendar = new ClinicCalendar(options.Value.ClinicTimeZone);
    }

    // Days 29 to 31 belong to Week 4, so every month has exactly four weeks.
    public static int WeekOfMonth(int dayOfMonth) =>
        Math.Min(((dayOfMonth - 1) / DaysInWeek) + 1, WeeksInReport);

    public async Task<MonthlyReportResponse> GetAsync(DateOnly month, CancellationToken cancellationToken = default)
    {
        var firstDay = new DateOnly(month.Year, month.Month, 1);
        var fromUtc = _calendar.StartOfDayUtc(firstDay);
        var toUtc = _calendar.StartOfDayUtc(firstDay.AddMonths(1));

        var events = await _dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.OccurredAt >= fromUtc && notification.OccurredAt < toUtc)
            .Where(notification => notification.Type == NotificationType.PatientCheckedIn
                || notification.Type == NotificationType.ConsultationCompleted)
            .Select(notification => new MonthEvent(
                notification.Type,
                notification.PatientId,
                notification.IsNewPatient,
                notification.Diagnosis,
                notification.OccurredAt))
            .ToListAsync(cancellationToken);

        // One entry per patient: whether they registered this month, and the week they first came.
        var patients = events
            .Where(item => item.Type == NotificationType.PatientCheckedIn)
            .GroupBy(item => item.PatientId)
            .Select(visits => new PatientMonth(
                visits.Any(visit => visit.IsNewPatient == true),
                WeekOfMonth(_calendar.DateOf(visits.Min(visit => visit.OccurredAt)).Day)))
            .ToList();

        var newPatients = patients.Count(patient => patient.IsNew);
        var weeklyBreakdown = Enumerable.Range(1, WeeksInReport)
            .Select(week => new WeekPatientCount(week, patients.Count(patient => patient.FirstWeek == week)))
            .ToList();

        return new MonthlyReportResponse(
            firstDay.ToString(MonthFormat, CultureInfo.InvariantCulture),
            patients.Count,
            newPatients,
            patients.Count - newPatients,
            DiagnosisRanking.Top(
                events
                    .Where(item => item.Type == NotificationType.ConsultationCompleted)
                    .Select(item => item.Diagnosis),
                TopDiagnosesLimit),
            weeklyBreakdown);
    }

    private sealed record MonthEvent(
        NotificationType Type,
        Guid PatientId,
        bool? IsNewPatient,
        string? Diagnosis,
        DateTime OccurredAt);

    private sealed record PatientMonth(bool IsNew, int FirstWeek);
}
