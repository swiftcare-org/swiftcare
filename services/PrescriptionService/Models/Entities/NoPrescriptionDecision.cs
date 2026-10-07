namespace PrescriptionService.Models.Entities;

// A doctor has recorded that a completed consultation needs no medicine. It is kept apart
// from Prescription so that pending lists, dispensing, history and reports, which all read
// prescriptions, are unaffected by it.
public sealed class NoPrescriptionDecision
{
    public const string NotRequiredStatus = "NOT_REQUIRED";

    public Guid Id { get; set; }
    public Guid ConsultationId { get; set; }
    public Guid QueueId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public required string DoctorName { get; set; }
    public DateTime RecordedAt { get; set; }
}
