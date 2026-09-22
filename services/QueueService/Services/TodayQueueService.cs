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

    public async Task<CalledPatientResponse?> GetCurrentForDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        if (doctorId == Guid.Empty)
        {
            throw new ArgumentException("Doctor ID must be provided.", nameof(doctorId));
        }

        var clinicNow = TimeZoneInfo.ConvertTime(
            _timeProvider.GetUtcNow(),
            _clinicTimeZone);
        var queueDate = DateOnly.FromDateTime(clinicNow.DateTime);

        var entry = await _dbContext.QueueEntries
            .AsNoTracking()
            .Where(candidate =>
                candidate.QueueDate == queueDate
                && candidate.Status == QueueStatus.InConsultation
                && candidate.DoctorId == doctorId)
            .OrderByDescending(candidate => candidate.CalledAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (entry is null)
        {
            return null;
        }

        return new CalledPatientResponse
        {
            QueueId = entry.Id,
            PatientId = entry.PatientId,
            QueueNumber = entry.QueueNumber,
            Status = "IN_CONSULTATION",
            DoctorId = doctorId,
            DoctorName = entry.DoctorName ?? throw new InvalidOperationException(
                "The active queue entry has no doctor name."),
            RoomNumber = entry.RoomNumber ?? throw new InvalidOperationException(
                "The active queue entry has no room number."),
            CalledAt = DateTime.SpecifyKind(
                entry.CalledAt ?? throw new InvalidOperationException(
                    "The active queue entry has no call time."),
                DateTimeKind.Utc)
        };
    }

    public async Task<WaitingRoomDisplayResponse> GetDisplayAsync(
        CancellationToken cancellationToken = default)
    {
        var clinicNow = TimeZoneInfo.ConvertTime(
            _timeProvider.GetUtcNow(),
            _clinicTimeZone);
        var queueDate = DateOnly.FromDateTime(clinicNow.DateTime);

        // Select only public-display fields so patient and doctor identifiers never enter
        // the display read model or its serialized response.
        var entries = await _dbContext.QueueEntries
            .AsNoTracking()
            .Where(entry =>
                entry.QueueDate == queueDate
                && (entry.Status == QueueStatus.Waiting
                    || entry.Status == QueueStatus.InConsultation))
            .Select(entry => new
            {
                entry.QueueNumber,
                entry.Status,
                entry.RoomNumber
            })
            .ToListAsync(cancellationToken);

        var currentRooms = entries
            .Where(entry =>
                entry.Status == QueueStatus.InConsultation
                && !string.IsNullOrWhiteSpace(entry.RoomNumber))
            .OrderBy(entry => entry.RoomNumber!.Length)
            .ThenBy(entry => entry.RoomNumber)
            .Select(entry => new RoomQueueAssignmentResponse
            {
                RoomNumber = entry.RoomNumber!,
                QueueNumber = entry.QueueNumber
            })
            .ToList();

        var nextQueueNumbers = entries
            .Where(entry => entry.Status == QueueStatus.Waiting)
            .OrderBy(entry => entry.QueueNumber.Length)
            .ThenBy(entry => entry.QueueNumber)
            .Take(3)
            .Select(entry => entry.QueueNumber)
            .ToList();

        return new WaitingRoomDisplayResponse
        {
            CurrentRooms = currentRooms,
            NextQueueNumbers = nextQueueNumbers
        };
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
                DoctorName = entry.DoctorName,
                CompletedAt = entry.CompletedAt.HasValue
                    ? DateTime.SpecifyKind(entry.CompletedAt.Value, DateTimeKind.Utc)
                    : null
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
