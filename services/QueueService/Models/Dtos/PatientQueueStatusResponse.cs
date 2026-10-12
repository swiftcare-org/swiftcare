namespace QueueService.Models.Dtos;

public sealed class PatientQueueStatusResponse
{
    public bool IsCheckedIn { get; init; }
    public string? QueueNumber { get; init; }
    public string? Status { get; init; }
}
