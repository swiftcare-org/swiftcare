namespace PrescriptionService.Models.Entities;

public sealed class PrescriptionItem
{
    public Guid Id { get; set; }
    public Guid PrescriptionId { get; set; }
    public int ItemOrder { get; set; }
    public required string MedicineName { get; set; }
    public required string Dosage { get; set; }
    public required string Frequency { get; set; }
    public required string Duration { get; set; }
    public string? Instructions { get; set; }
    public Prescription Prescription { get; set; } = null!;
}
