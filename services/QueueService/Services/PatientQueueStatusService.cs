using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QueueService.Data;
using QueueService.Models.Configuration;
using QueueService.Models.Dtos;

namespace QueueService.Services;

public sealed class PatientQueueStatusService : IPatientQueueStatusService
{
    private readonly QueueDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _clinicTimeZone;

    public PatientQueueStatusService(
        QueueDbContext dbContext,
        IOptions<QueueOptions> options,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _clinicTimeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.ClinicTimeZone);
    }

    public async Task<PatientQueueStatusResponse> GetTodayStatusAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var clinicNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, _clinicTimeZone);
        var queueDate = DateOnly.FromDateTime(clinicNow);

        var status = await _dbContext.QueueEntries
            .AsNoTracking()
            .Where(entry => entry.PatientId == patientId && entry.QueueDate == queueDate)
            .Select(entry => new PatientQueueStatusResponse
            {
                IsCheckedIn = true,
                QueueNumber = entry.QueueNumber
            })
            .SingleOrDefaultAsync(cancellationToken);

        return status ?? new PatientQueueStatusResponse { IsCheckedIn = false };
    }
}
