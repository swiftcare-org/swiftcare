using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QueueService.Data;
using QueueService.Logging;
using QueueService.Models.Configuration;
using QueueService.Models.Dtos;
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
        // observing the same room as free or selecting the same first waiting patient.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var doctorOrRoomOccupied = await _dbContext.QueueEntries
            .AnyAsync(
                entry => entry.QueueDate == queueDate
                    && entry.Status == QueueStatus.InConsultation
                    && (entry.DoctorId == doctorId || entry.RoomNumber == normalizedRoomNumber),
                cancellationToken);

        if (doctorOrRoomOccupied)
        {
            _logger.LogInformation(
                "Call-next rejected because doctor or room is occupied: doctorId={DoctorId} roomNumber={RoomNumber}",
                doctorId,
                LogSanitizer.Sanitize(normalizedRoomNumber));
            return new CallNextPatientResult
            {
                Outcome = CallNextPatientOutcome.DoctorOrRoomOccupied
            };
        }

        var nextEntry = await _dbContext.QueueEntries
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
}
