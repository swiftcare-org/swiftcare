using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QueueService.Data;
using QueueService.Models.Entities;
using QueueService.Models.Events;

namespace QueueService.Services;

public static class OutboxDelivery
{
    public static async Task<bool> TryDeliverAsync(QueueDbContext context, IQueueEventPublisher publisher,
        OutboxMessage message, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<PatientCalledEvent>(message.Payload)
                ?? throw new InvalidOperationException("Pending event has no payload.");
            if (!await publisher.PublishPatientCalledAsync(payload, cancellationToken)) return false;
            context.OutboxMessages.Remove(message);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A failed acknowledgement leaves the original event available for redelivery.
            logger.LogWarning(exception, "Pending event delivery will be retried: eventId={EventId}", message.Id);
            return false;
        }
    }

    public static async Task<int> DeliverPendingAsync(QueueDbContext context, IQueueEventPublisher publisher,
        ILogger logger, CancellationToken cancellationToken = default)
    {
        var pending = await context.OutboxMessages.OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id).Take(100).ToListAsync(cancellationToken);
        var delivered = 0;
        foreach (var message in pending)
        {
            if (!await TryDeliverAsync(context, publisher, message, logger, cancellationToken)) break;
            delivered++;
        }
        return delivered;
    }
}
