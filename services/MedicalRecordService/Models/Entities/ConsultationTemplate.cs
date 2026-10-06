namespace MedicalRecordService.Models.Entities;

public sealed class ConsultationTemplate
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Symptoms { get; set; }
    public required string ExaminationFindings { get; set; }
    public required string Notes { get; set; }

    // Null for a built-in template, which every doctor can use and nobody can remove.
    public Guid? CreatedByDoctorId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
