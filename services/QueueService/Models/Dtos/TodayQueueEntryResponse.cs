namespace QueueService.Models.Dtos;

public sealed class TodayQueueEntryResponse
{
    public required Guid QueueId { get; init; }
    public required Guid PatientId { get; init; }
    public required string QueueNumber { get; init; }
    public required DateTime CheckedInAt { get; init; }
    public required string Status { get; init; }
    public string? RoomNumber { get; init; }
    public string? DoctorName { get; init; }
}
