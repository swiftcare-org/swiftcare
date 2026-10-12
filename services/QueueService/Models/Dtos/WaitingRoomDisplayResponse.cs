namespace QueueService.Models.Dtos;

public sealed class WaitingRoomDisplayResponse
{
    public required IReadOnlyList<RoomQueueAssignmentResponse> CurrentRooms { get; init; }
    public required IReadOnlyList<string> NextQueueNumbers { get; init; }
}

public sealed class RoomQueueAssignmentResponse
{
    public required string RoomNumber { get; init; }
    public required string QueueNumber { get; init; }
}
