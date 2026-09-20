using Microsoft.EntityFrameworkCore;
using QueueService.Data;
using QueueService.Models.Entities;
using QueueService.Models.Enums;
using QueueService.Models.Events;

namespace QueueService.Services;

public sealed class QueueCompletionService : IQueueCompletionService
{
    private readonly QueueDbContext _dbContext;

    public QueueCompletionService(QueueDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<QueueCompletionOutcome> CompleteAsync(
        ConsultationCompletedEvent completedEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completedEvent);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (await _dbContext.ProcessedEvents.AnyAsync(
                item => item.EventId == completedEvent.EventId, cancellationToken))
        {
            return QueueCompletionOutcome.DuplicateEvent;
        }

        var entry = await _dbContext.QueueEntries.SingleOrDefaultAsync(
            item => item.Id == completedEvent.QueueId, cancellationToken);

        if (entry is null
            || entry.PatientId != completedEvent.PatientId
            || entry.DoctorId != completedEvent.DoctorId)
        {
            // Do not mark this event processed: the queue assignment may not yet be
            // visible, and retry must never complete a different patient's entry.
            throw new InvalidOperationException(
                "Consultation completion does not match a queue assignment.");
        }

        if (entry.Status is not (QueueStatus.InConsultation or QueueStatus.Completed))
        {
            throw new InvalidOperationException(
                "Queue entry is not in consultation or completed.");
        }

        var outcome = entry.Status == QueueStatus.Completed
            ? QueueCompletionOutcome.AlreadyCompleted
            : QueueCompletionOutcome.Completed;

        if (outcome == QueueCompletionOutcome.Completed)
        {
            entry.Status = QueueStatus.Completed;
        }

        _dbContext.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = completedEvent.EventId,
            ProcessedAt = DateTime.UtcNow
        });

        // Status and the event ID are committed together. Redelivery is a no-op.
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }
}
