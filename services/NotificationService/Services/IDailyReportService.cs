using NotificationService.Models.Dtos;

namespace NotificationService.Services;

public interface IDailyReportService
{
    // The report for one clinic day. A day with no activity gives zero totals, never null.
    Task<DailyReportResponse> GetAsync(DateOnly date, CancellationToken cancellationToken = default);
}
