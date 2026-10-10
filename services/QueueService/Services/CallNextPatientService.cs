using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using QueueService.Data;
using QueueService.Logging;
using QueueService.Models.Configuration;
using QueueService.Models.Dtos;
using QueueService.Models.Entities;
using QueueService.Models.Enums;
using QueueService.Models.Events;

namespace QueueService.Services;

public sealed class CallNextPatientService : ICallNextPatientService
{
    private readonly QueueDbContext _dbContext;
    private readonly IQueueEventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _clinicTimeZone;
    private readonly ILogger<CallNextPatientService> _logger;
    private readonly int _maxAttempts;

    public CallNextPatientService(
        QueueDbContext dbContext,
        IQueueEventPublisher eventPublisher,
        IOptions<QueueOptions> options,
        TimeProvider timeProvider,
        ILogger<CallNextPatientService> logger)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _timeProvider = timeProvider;
        _clinicTimeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.ClinicTimeZone);
        _logger = logger;
        _maxAttempts = Math.Max(1, options.Value.MaxCallNextAttempts);
    }

    public async Task<CallNextPatientResult> CallNextAsync(
        Guid doctorId,
        string doctorName,
        string roomNumber,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (doctorId == Guid.Empty)
        {
            throw new ArgumentException("Doctor ID must be provided.", nameof(doctorId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(doctorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(roomNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var normalizedDoctorName = doctorName.Trim();
        var normalizedRoomNumber = roomNumber.Trim();
        var now = _timeProvider.GetUtcNow();
        var utcNow = now.UtcDateTime;
        var clinicNow = TimeZoneInfo.ConvertTime(
            now,
            _clinicTimeZone);
        var queueDate = DateOnly.FromDateTime(clinicNow.DateTime);

        // Serializable isolation prevents two simultaneous call-next requests from both
        // observing the same room as free or selecting the same first waiting patient. The
        // price is that MySQL resolves such a collision by rolling one of them back as a
        // deadlock. That attempt is simply run again: by then the other doctor's call has
        // committed, so the retry sees their patient as taken and calls the next one.
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            QueueEntry? nextEntry;
            try
            {
                var refusal = await FindRefusalAsync(doctorId, normalizedRoomNumber, queueDate, cancellationToken);
                if (refusal is not null)
                {
                    return refusal;
                }

                nextEntry = await _dbContext.QueueEntries
                    .Where(entry => entry.QueueDate == queueDate && entry.Status == QueueStatus.Waiting)
                    .OrderBy(entry => entry.QueueNumber.Length)
                    .ThenBy(entry => entry.QueueNumber)
                    .FirstOrDefaultAsync(cancellationToken);

                if (nextEntry is null)
                {
                    return new CallNextPatientResult
                    {
                        Outcome = CallNextPatientOutcome.NoPatientsWaiting
                    };
                }

                nextEntry.Status = QueueStatus.InConsultation;
                nextEntry.DoctorId = doctorId;
                nextEntry.DoctorName = normalizedDoctorName;
                nextEntry.RoomNumber = normalizedRoomNumber;
                nextEntry.CalledAt = utcNow;

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (LockConflictDetector.IsLockConflict(exception))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                // The rolled-back assignment must not be saved by the next attempt.
                _dbContext.ChangeTracker.Clear();

                if (attempt < _maxAttempts)
                {
                    _logger.LogInformation(
                        "Call-next collided with another call and is retried: doctorId={DoctorId} attempt={Attempt}",
                        doctorId,
                        attempt);
                    continue;
                }

                _logger.LogWarning(
                    "Call-next gave up after repeated collisions: doctorId={DoctorId} attempts={Attempts}",
                    doctorId,
                    attempt);
                return new CallNextPatientResult
                {
                    Outcome = CallNextPatientOutcome.ConcurrentCallConflict
                };
            }

            return await PublishAndCommitAsync(
                transaction,
                nextEntry,
                new Assignment(doctorId, normalizedDoctorName, normalizedRoomNumber, utcNow, correlationId),
                cancellationToken);
        }
    }

    private async Task<CallNextPatientResult?> FindRefusalAsync(
        Guid doctorId,
        string roomNumber,
        DateOnly queueDate,
        CancellationToken cancellationToken)
    {
        var doctorOrRoomOccupied = await _dbContext.QueueEntries
            .AnyAsync(
                entry => entry.QueueDate == queueDate
                    && entry.Status == QueueStatus.InConsultation
                    && (entry.DoctorId == doctorId || entry.RoomNumber == roomNumber),
                cancellationToken);

        if (!doctorOrRoomOccupied)
        {
            return null;
        }

        _logger.LogInformation(
            "Call-next rejected because doctor or room is occupied: doctorId={DoctorId} roomNumber={RoomNumber}",
            doctorId,
            LogSanitizer.Sanitize(roomNumber));
        return new CallNextPatientResult
        {
            Outcome = CallNextPatientOutcome.DoctorOrRoomOccupied
        };
    }

    // The assignment is saved but not committed: it only becomes real once the
    // patient-called event is out, so the waiting room and the queue never disagree.
    private async Task<CallNextPatientResult> PublishAndCommitAsync(
        IDbContextTransaction transaction,
        QueueEntry nextEntry,
        Assignment assignment,
        CancellationToken cancellationToken)
    {
        var (doctorId, normalizedDoctorName, normalizedRoomNumber, utcNow, correlationId) = assignment;

        var patientCalledEvent = new PatientCalledEvent
        {
            EventId = Guid.NewGuid(),
            QueueId = nextEntry.Id,
            PatientId = nextEntry.PatientId,
            QueueNumber = nextEntry.QueueNumber,
            DoctorId = doctorId,
            DoctorName = normalizedDoctorName,
            RoomNumber = normalizedRoomNumber,
            CalledAt = utcNow,
            CorrelationId = correlationId
        };

        var published = await _eventPublisher.PublishPatientCalledAsync(
            patientCalledEvent,
            cancellationToken);

        if (!published)
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            return new CallNextPatientResult
            {
                Outcome = CallNextPatientOutcome.EventPublishFailed
            };
        }

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Patient called from waiting pool: queueId={QueueId} patientId={PatientId} doctorId={DoctorId} roomNumber={RoomNumber} correlationId={CorrelationId}",
            nextEntry.Id,
            nextEntry.PatientId,
            doctorId,
            LogSanitizer.Sanitize(normalizedRoomNumber),
            LogSanitizer.Sanitize(correlationId));

        return new CallNextPatientResult
        {
            Outcome = CallNextPatientOutcome.Success,
            CalledPatient = new CalledPatientResponse
            {
                QueueId = nextEntry.Id,
                PatientId = nextEntry.PatientId,
                QueueNumber = nextEntry.QueueNumber,
                Status = "IN_CONSULTATION",
                DoctorId = doctorId,
                DoctorName = normalizedDoctorName,
                RoomNumber = normalizedRoomNumber,
                CalledAt = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)
            }
        };
    }

    private sealed record Assignment(
        Guid DoctorId,
        string DoctorName,
        string RoomNumber,
        DateTime CalledAtUtc,
        string CorrelationId);
}
