namespace MedicalRecordService.Models.Entities;

public sealed class Consultation
{
    public const string InProgressStatus = "IN_PROGRESS";
    public const string CompleteStatus = "COMPLETE";

    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid QueueId { get; set; }
    public Guid DoctorId { get; set; }
    public required string DoctorName { get; set; }
    public required string RoomNumber { get; set; }
    public required string Symptoms { get; set; }
    public string? ExaminationFindings { get; set; }
    public required string Diagnosis { get; set; }
    public string? Notes { get; set; }
    public DateOnly? FollowUpDate { get; set; }
    public string? FollowUpInstructions { get; set; }
    public Guid? TemplateId { get; set; }
    public string? TemplateName { get; set; }
    public DateTime ConsultationDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = InProgressStatus;
    public Guid? EventId { get; set; }
    public VitalSigns? VitalSigns { get; set; }
}
