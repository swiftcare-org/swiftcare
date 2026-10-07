namespace NotificationService.Models.Dtos;

// One entry of the activity feed. It carries the patient's ID, not their name: the events
// this is built from hold no demographics, so the reader looks the name up in PatientService.
public sealed record NotificationResponse(
    Guid Id,
    string Type,
    Guid PatientId,
    DateTime OccurredAt,
    bool? IsNewPatient = null,
    string? QueueNumber = null,
    string? DoctorName = null,
    string? RoomNumber = null);
