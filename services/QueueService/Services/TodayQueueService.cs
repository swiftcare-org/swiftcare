using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QueueService.Data;
using QueueService.Models.Configuration;
using QueueService.Models.Dtos;
using QueueService.Models.Enums;

namespace QueueService.Services;

public sealed class TodayQueueService : ITodayQueueService
{
    private readonly QueueDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _clinicTimeZone;

    public TodayQueueService(
        QueueDbContext dbContext,
        IOptions<QueueOptions> options,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _clinicTimeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.ClinicTimeZone);
    }

    public async Task<IReadOnlyList<TodayQueueEntryResponse>> GetTodayAsync(
        CancellationToken cancellationToken = default)
    {
        return await GetTodayEntriesAsync(status: null, cancellationToken);
    }

    public async Task<IReadOnlyList<TodayQueueEntryResponse>> GetWaitingAsync(
        CancellationToken cancellationToken = default)
    {
        return await GetTodayEntriesAsync(QueueStatus.Waiting, cancellationToken);
    }

    private async Task<IReadOnlyList<TodayQueueEntryResponse>> GetTodayEntriesAsync(
        QueueStatus? status,
        CancellationToken cancellationToken)
    {
        var clinicNow = TimeZoneInfo.ConvertTime(
            _timeProvider.GetUtcNow(),
            _clinicTimeZone);
        var queueDate = DateOnly.FromDateTime(clinicNow.DateTime);

        var query = _dbContext.QueueEntries
            .AsNoTracking()
            .Where(entry => entry.QueueDate == queueDate);

        if (status.HasValue)
        {
            query = query.Where(entry => entry.Status == status.Value);
        }

        var entries = await query
            // Queue numbers are zero-padded to three digits but may grow beyond Q-999.
            // Length followed by ordinal value preserves numeric order across that boundary.
            .OrderBy(entry => entry.QueueNumber.Length)
            .ThenBy(entry => entry.QueueNumber)
            .ToListAsync(cancellationToken);

        return entries
            .Select(entry => new TodayQueueEntryResponse
            {
                QueueId = entry.Id,
                PatientId = entry.PatientId,
                QueueNumber = entry.QueueNumber,
                CheckedInAt = DateTime.SpecifyKind(entry.CheckedInAt, DateTimeKind.Utc),
                Status = ToApiStatus(entry.Status),
                RoomNumber = entry.RoomNumber,
                DoctorName = entry.DoctorName
            })
            .ToList();
    }

    private static string ToApiStatus(QueueStatus status) => status switch
    {
        QueueStatus.Waiting => "WAITING",
        QueueStatus.InConsultation => "IN_CONSULTATION",
        QueueStatus.Completed => "COMPLETED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported queue status.")
    };
}
