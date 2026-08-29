using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QueueService.Data;
using QueueService.Models.Configuration;
using QueueService.Models.Dtos;
using QueueService.Models.Entities;
using QueueService.Models.Enums;

namespace QueueService.Services;

public sealed class QueueEntryCreationService : IQueueEntryCreationService
{
    private readonly QueueDbContext _dbContext;
    private readonly QueueOptions _options;
    private readonly TimeZoneInfo _clinicTimeZone;
    private readonly ILogger<QueueEntryCreationService> _logger;

    public QueueEntryCreationService(
        QueueDbContext dbContext,
        IOptions<QueueOptions> options,
        ILogger<QueueEntryCreationService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        // Resolved once here rather than per call: a bad zone ID fails on the first
        // invocation instead of silently misdating every queue entry it processes.
        _clinicTimeZone = TimeZoneInfo.FindSystemTimeZoneById(_options.ClinicTimeZone);
        _logger = logger;
    }

    public async Task<QueueEntryCreationResult> CreateQueueEntryAsync(
        Guid eventId,
        Guid patientId,
        DateTime checkedInAtUtc,
        CancellationToken cancellationToken = default)
    {
        // Scenario 3: the same Kafka message redelivered is recognized here, before any
        // queue entry is created, regardless of how the first delivery was processed.
        var alreadyProcessed = await _dbContext.ProcessedEvents
            .AnyAsync(e => e.EventId == eventId, cancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "Skipped duplicate patient-checked-in event: eventId={EventId} patientId={PatientId}",
                eventId,
                patientId);
            return new QueueEntryCreationResult { Outcome = QueueEntryCreationOutcome.DuplicateEvent };
        }

        var queueDate = ToClinicDate(checkedInAtUtc);

        for (var attempt = 1; attempt <= _options.MaxAllocationAttempts; attempt++)
        {
            // Discards anything tracked by a failed prior attempt in this loop, so every
            // attempt reads the counter's current committed value instead of retrying
            // against stale in-memory state that the rolled-back transaction invalidated.
            _dbContext.ChangeTracker.Clear();

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var result = await TryCreateEntryAsync(eventId, patientId, queueDate, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (DbUpdateConcurrencyException) when (attempt < _options.MaxAllocationAttempts)
            {
                // Another consumer instance won the compare-and-swap on today's
                // DailyQueueCounter row. Retry from a clean read rather than surfacing this
                // as a failure - it is expected under concurrent check-ins, not an error.
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Scenario 4's final backstop: UNIQUE(PatientId, QueueDate) rejected a second
                // entry for a patient already queued today that the AnyAsync check above
                // raced past (e.g. two distinct events for the same patient processed close
                // together). The patient is already queued either way, so this is a skip,
                // not a failure.
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogInformation(
                    "Skipped check-in rejected by the unique constraint: eventId={EventId} patientId={PatientId} queueDate={QueueDate}",
                    eventId,
                    patientId,
                    queueDate);
                return new QueueEntryCreationResult { Outcome = QueueEntryCreationOutcome.AlreadyQueuedToday };
            }
        }

        throw new InvalidOperationException(
            $"Failed to allocate a queue number for {queueDate:yyyy-MM-dd} after {_options.MaxAllocationAttempts} attempts due to concurrent contention.");
    }

    private async Task<QueueEntryCreationResult> TryCreateEntryAsync(
        Guid eventId,
        Guid patientId,
        DateOnly queueDate,
        CancellationToken cancellationToken)
    {
        var alreadyQueuedToday = await _dbContext.QueueEntries
            .AnyAsync(e => e.PatientId == patientId && e.QueueDate == queueDate, cancellationToken);

        if (alreadyQueuedToday)
        {
            // Recorded so a redelivery of this same event short-circuits at the eventId
            // check above instead of re-running this query on every replay.
            _dbContext.ProcessedEvents.Add(new ProcessedEvent { EventId = eventId, ProcessedAt = DateTime.UtcNow });
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Skipped check-in: patient already queued today: eventId={EventId} patientId={PatientId} queueDate={QueueDate}",
                eventId,
                patientId,
                queueDate);
            return new QueueEntryCreationResult { Outcome = QueueEntryCreationOutcome.AlreadyQueuedToday };
        }

        var counter = await _dbContext.DailyQueueCounters
            .SingleOrDefaultAsync(c => c.QueueDate == queueDate, cancellationToken);

        if (counter is null)
        {
            counter = new DailyQueueCounter { QueueDate = queueDate, LastNumber = 0 };
            _dbContext.DailyQueueCounters.Add(counter);

            try
            {
                // Saved immediately so the new row has a committed LastNumber to serve as
                // the concurrency token baseline for the increment below - otherwise
                // "insert" and "increment" would be indistinguishable from a lost update.
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
            {
                // A different allocator created today's counter row between our
                // SingleOrDefaultAsync read and this insert (first check-in of a new day
                // from two different patients, racing). This is a concurrency race, not a
                // genuine duplicate check-in, so it must be retried rather than reported
                // as AlreadyQueuedToday by the generic DbUpdateException handler below.
                throw new DbUpdateConcurrencyException(
                    "DailyQueueCounter row for this date was created concurrently.", ex);
            }
        }

        counter.LastNumber++;
        var queueNumber = $"Q-{counter.LastNumber:D3}";

        _dbContext.QueueEntries.Add(new QueueEntry
        {
            PatientId = patientId,
            QueueDate = queueDate,
            QueueNumber = queueNumber,
            Status = QueueStatus.Waiting,
            RoomNumber = null
        });
        _dbContext.ProcessedEvents.Add(new ProcessedEvent { EventId = eventId, ProcessedAt = DateTime.UtcNow });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Queue entry created: eventId={EventId} patientId={PatientId} queueDate={QueueDate} queueNumber={QueueNumber}",
            eventId,
            patientId,
            queueDate,
            queueNumber);

        return new QueueEntryCreationResult { Outcome = QueueEntryCreationOutcome.Created, QueueNumber = queueNumber };
    }

    private DateOnly ToClinicDate(DateTime checkedInAtUtc)
    {
        var utc = DateTime.SpecifyKind(checkedInAtUtc, DateTimeKind.Utc);
        var clinicLocal = TimeZoneInfo.ConvertTimeFromUtc(utc, _clinicTimeZone);
        return DateOnly.FromDateTime(clinicLocal);
    }
}
