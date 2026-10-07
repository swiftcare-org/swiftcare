namespace NotificationService.Models.Events;

// NotificationService's own copy of PatientService's event contract, not a shared project
// reference. If the publisher changes this shape, the event fails validation here and is
// logged and skipped, instead of breaking silently through a shared dependency.
public sealed class PatientCheckedInEvent
{
    public Guid EventId { get; init; }
    public Guid PatientId { get; init; }
    public bool IsNewPatient { get; init; }
    public DateTime CheckedInAt { get; init; }
}
