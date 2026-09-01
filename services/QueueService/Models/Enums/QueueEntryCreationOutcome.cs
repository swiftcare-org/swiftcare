namespace QueueService.Models.Enums;

public enum QueueEntryCreationOutcome
{
    Created,
    DuplicateEvent,
    AlreadyQueuedToday
}
