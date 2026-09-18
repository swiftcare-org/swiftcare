namespace MedicalRecordService.Models.Entities;

public sealed class VitalSigns
{
    public Guid Id { get; set; }
    public Guid ConsultationId { get; set; }
    public int? SystolicBloodPressure { get; set; }
    public int? DiastolicBloodPressure { get; set; }
    public decimal? TemperatureCelsius { get; set; }
    public int? PulseRate { get; set; }
    public int? RespiratoryRate { get; set; }
    public int? OxygenSaturation { get; set; }
    public decimal? HeightCentimeters { get; set; }
    public decimal? WeightKilograms { get; set; }
    public decimal? Bmi { get; set; }
    public DateTime RecordedAt { get; set; }
    public Consultation Consultation { get; set; } = null!;
}
