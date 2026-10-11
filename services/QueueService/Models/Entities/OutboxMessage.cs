using System.Text.Json;
using QueueService.Models.Events;

namespace QueueService.Models.Entities;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public required string Payload { get; set; }
    public DateTime CreatedAt { get; set; }

    public static OutboxMessage Create(Guid id, PatientCalledEvent message, DateTime occurredAt) =>
        new() { Id = id, Payload = JsonSerializer.Serialize(message), CreatedAt = occurredAt };
}
