namespace PrescriptionService.Models.Entities;

public sealed class Prescription
{
    public const string PendingStatus = "PENDING";

    public Guid Id { get; set; }
    public Guid ConsultationId { get; set; }
    public Guid QueueId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public required string DoctorName { get; set; }
    public string Status { get; set; } = PendingStatus;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<PrescriptionItem> Items { get; } = new List<PrescriptionItem>();
}
