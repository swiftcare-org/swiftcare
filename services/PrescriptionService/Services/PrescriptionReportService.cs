using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrescriptionService.Data;
using PrescriptionService.Models.Configuration;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Models.Entities;

namespace PrescriptionService.Services;

public sealed class PrescriptionReportService : IPrescriptionReportService
{
    private readonly PrescriptionDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _clinicTimeZone;

    public PrescriptionReportService(
        PrescriptionDbContext dbContext,
        IOptions<ClinicOptions> options,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _clinicTimeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
    }

    public async Task<PrescriptionDailyReportResponse> GetDailyReportAsync(
        DateOnly? date,
        CancellationToken cancellationToken = default)
    {
        var clinicDate = date ?? DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _clinicTimeZone).DateTime);

        // CreatedAt is stored in UTC, so the clinic's calendar day is turned into the UTC
        // range it covers. A prescription written at 00:30 clinic time belongs to that day
        // even though its UTC timestamp falls on the previous date.
        var startUtc = ToUtc(clinicDate);
        var endUtc = ToUtc(clinicDate.AddDays(1));

        var countsByStatus = await _dbContext.Prescriptions
            .AsNoTracking()
            .Where(prescription => prescription.CreatedAt >= startUtc && prescription.CreatedAt < endUtc)
            .GroupBy(prescription => prescription.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var dispensed = countsByStatus
            .Where(entry => entry.Status == Prescription.DispensedStatus)
            .Sum(entry => entry.Count);
        var pending = countsByStatus
            .Where(entry => entry.Status == Prescription.PendingStatus)
            .Sum(entry => entry.Count);

        return new PrescriptionDailyReportResponse(
            clinicDate,
            TotalWritten: countsByStatus.Sum(entry => entry.Count),
            TotalDispensed: dispensed,
            TotalPending: pending);
    }

    private DateTime ToUtc(DateOnly clinicDate) => TimeZoneInfo.ConvertTimeToUtc(
        clinicDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
        _clinicTimeZone);
}
