using QueueService.Models.Enums;

namespace QueueService.Models.Dtos;

public sealed class QueueEntryCreationResult
{
    public required QueueEntryCreationOutcome Outcome { get; init; }

    // Only populated when Outcome is Created - callers log it, nothing else reads it.
    public string? QueueNumber { get; init; }
}
