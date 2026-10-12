namespace QueueService.Models.Entities;

// Shared idempotency ledger for patient check-ins and consultation completions.
public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }
    public DateTime ProcessedAt { get; set; }
}
