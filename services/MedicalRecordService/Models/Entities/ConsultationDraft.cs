namespace MedicalRecordService.Models.Entities;

public sealed class ConsultationDraft
{
    public required Guid Id { get; init; }
    public required Guid QueueId { get; init; }
    public required Guid PatientId { get; init; }
    public required Guid DoctorId { get; init; }
    public required string DoctorName { get; init; }
    public required string RoomNumber { get; init; }
    public required string Symptoms { get; init; }
    public string? ExaminationFindings { get; init; }
    public required string Diagnosis { get; init; }
    public string? Notes { get; init; }
    public Guid? TemplateId { get; init; }
    public required DateTime ConsultationDate { get; init; }
}
