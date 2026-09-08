namespace QueueService.Models.Dtos;

public sealed class CalledPatientResponse
{
    public required Guid QueueId { get; init; }
    public required Guid PatientId { get; init; }
    public required string QueueNumber { get; init; }
    public required string Status { get; init; }
    public required Guid DoctorId { get; init; }
    public required string DoctorName { get; init; }
    public required string RoomNumber { get; init; }
    public required DateTime CalledAt { get; init; }
}
