using QueueService.Models.Enums;

namespace QueueService.Models.Entities;

public sealed class QueueEntry : IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // No foreign key to Patients: PatientService owns that table in a different database,
    // and a service must never query or reference another service's schema directly.
    public required Guid PatientId { get; set; }

    // Clinic-local calendar date (see QueueOptions.ClinicTimeZone), not the UTC date the
    // triggering event's CheckedInAt carries - a UTC-date reset would roll the daily
    // sequence over at 05:30 local time in Sri Lanka instead of midnight.
    public DateOnly QueueDate { get; set; }

    public required string QueueNumber { get; set; }
    public QueueStatus Status { get; set; }

    // Preserves the time the receptionist checked the patient in. CreatedAt records when
    // QueueService processed the Kafka event, which can be later if delivery was delayed.
    public DateTime CheckedInAt { get; set; }

    public string? RoomNumber { get; set; }
    public Guid? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public DateTime? CalledAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
