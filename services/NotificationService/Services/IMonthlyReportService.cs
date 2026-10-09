using NotificationService.Models.Dtos;

namespace NotificationService.Services;

public interface IMonthlyReportService
{
    // The report for the calendar month that contains the given date. A month with no
    // activity gives zero totals and four empty weeks, never null.
    Task<MonthlyReportResponse> GetAsync(DateOnly month, CancellationToken cancellationToken = default);
}
