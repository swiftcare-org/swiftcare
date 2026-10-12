namespace MedicalRecordService.Models.Dtos;

public sealed class VitalSignsResponse
{
    public required Guid Id { get; init; }
    public required Guid ConsultationId { get; init; }
    public int? SystolicBloodPressure { get; init; }
    public int? DiastolicBloodPressure { get; init; }
    public decimal? TemperatureCelsius { get; init; }
    public int? PulseRate { get; init; }
    public int? RespiratoryRate { get; init; }
    public int? OxygenSaturation { get; init; }
    public decimal? HeightCentimeters { get; init; }
    public decimal? WeightKilograms { get; init; }
    public decimal? Bmi { get; init; }
    public required DateTime RecordedAt { get; init; }
}
