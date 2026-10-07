using NotificationService.Models.Enums;

namespace NotificationService.Models.Entities;

// One department event, stored as it arrived from Kafka. It holds identifiers and the
// assignment details the events carry, never patient demographics or clinical notes:
// a reader that needs the patient's name asks PatientService for it.
public sealed class Notification
{
    public Guid Id { get; set; }

    // The publisher's event ID. Kafka can deliver an event more than once, and the unique
    // index on this column is what keeps a redelivery from being stored twice.
    public Guid EventId { get; set; }

    public NotificationType Type { get; set; }
    public Guid PatientId { get; set; }

    // Check-in events only.
    public bool? IsNewPatient { get; set; }

    // Patient-called and consultation-completed events.
    public Guid? QueueId { get; set; }
    public Guid? DoctorId { get; set; }

    // Patient-called events only.
    public string? QueueNumber { get; set; }
    public string? DoctorName { get; set; }
    public string? RoomNumber { get; set; }

    // Consultation-completed events only.
    public Guid? ConsultationId { get; set; }

    // When the event happened, in UTC. The consultation-completed event carries no
    // timestamp of its own, so for that type this is when the service received it.
    public DateTime OccurredAt { get; set; }

    public DateTime ReceivedAt { get; set; }
}
