using PrescriptionService.Models.Dtos;

namespace PrescriptionService.Services;

public interface IPrescriptionReportService
{
    // A null date means today in the clinic's time zone.
    Task<PrescriptionDailyReportResponse> GetDailyReportAsync(
        DateOnly? date,
        CancellationToken cancellationToken = default);
}
