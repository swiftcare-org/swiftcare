using System.Text.Json;
using PatientService.Models.Events;

namespace PatientService.Models.Entities;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public required string Payload { get; set; }
    public DateTime CreatedAt { get; set; }

    public static OutboxMessage Create(Guid id, PatientCheckedInEvent message, DateTime occurredAt) =>
        new() { Id = id, Payload = JsonSerializer.Serialize(message), CreatedAt = occurredAt };
}
