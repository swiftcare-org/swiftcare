namespace QueueService.Models.Entities;

// The Scenario 3 idempotency ledger: a Kafka message redelivered with the same EventId is
// recognized and skipped here before any queue entry is created.
public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }
    public DateTime ProcessedAt { get; set; }
}
