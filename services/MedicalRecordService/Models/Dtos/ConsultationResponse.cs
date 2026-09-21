namespace MedicalRecordService.Models.Dtos;

public sealed class ConsultationResponse
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
    public DateOnly? FollowUpDate { get; init; }
    public string? FollowUpInstructions { get; init; }
    public Guid? TemplateId { get; init; }
    public string? TemplateName { get; init; }
    public required DateTime ConsultationDate { get; init; }
}
