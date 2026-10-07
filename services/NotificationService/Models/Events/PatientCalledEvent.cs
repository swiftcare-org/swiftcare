namespace NotificationService.Models.Events;

// NotificationService's own copy of QueueService's event contract.
public sealed class PatientCalledEvent
{
    public Guid EventId { get; init; }
    public Guid QueueId { get; init; }
    public Guid PatientId { get; init; }
    public string? QueueNumber { get; init; }
    public Guid DoctorId { get; init; }
    public string? DoctorName { get; init; }
    public string? RoomNumber { get; init; }
    public DateTime CalledAt { get; init; }
}
