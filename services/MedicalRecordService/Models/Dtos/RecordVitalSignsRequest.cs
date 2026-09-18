using System.ComponentModel.DataAnnotations;

namespace MedicalRecordService.Models.Dtos;

public sealed class RecordVitalSignsRequest : IValidatableObject
{
    public int? SystolicBloodPressure { get; init; }
    public int? DiastolicBloodPressure { get; init; }
    public decimal? TemperatureCelsius { get; init; }
    public int? PulseRate { get; init; }
    public int? RespiratoryRate { get; init; }
    public int? OxygenSaturation { get; init; }
    public decimal? HeightCentimeters { get; init; }
    public decimal? WeightKilograms { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!HasAnyMeasurement())
        {
            yield return new ValidationResult(
                "At least one vital sign is required",
                MeasurementMemberNames);
        }

        if (SystolicBloodPressure.HasValue != DiastolicBloodPressure.HasValue)
        {
            yield return new ValidationResult(
                "Blood pressure requires both systolic and diastolic values",
                [nameof(SystolicBloodPressure), nameof(DiastolicBloodPressure)]);
        }

        foreach (var result in PositiveValueValidationResults())
        {
            yield return result;
        }
    }

    private static readonly string[] MeasurementMemberNames =
    [
        nameof(SystolicBloodPressure),
        nameof(DiastolicBloodPressure),
        nameof(TemperatureCelsius),
        nameof(PulseRate),
        nameof(RespiratoryRate),
        nameof(OxygenSaturation),
        nameof(HeightCentimeters),
        nameof(WeightKilograms)
    ];

    private bool HasAnyMeasurement() =>
        SystolicBloodPressure.HasValue
        || DiastolicBloodPressure.HasValue
        || TemperatureCelsius.HasValue
        || PulseRate.HasValue
        || RespiratoryRate.HasValue
        || OxygenSaturation.HasValue
        || HeightCentimeters.HasValue
        || WeightKilograms.HasValue;

    private IEnumerable<ValidationResult> PositiveValueValidationResults()
    {
        if (SystolicBloodPressure <= 0)
        {
            yield return PositiveValueError(
                nameof(SystolicBloodPressure),
                "Systolic blood pressure");
        }

        if (DiastolicBloodPressure <= 0)
        {
            yield return PositiveValueError(
                nameof(DiastolicBloodPressure),
                "Diastolic blood pressure");
        }

        if (TemperatureCelsius <= 0)
        {
            yield return PositiveValueError(nameof(TemperatureCelsius), "Temperature");
        }

        if (PulseRate <= 0)
        {
            yield return PositiveValueError(nameof(PulseRate), "Pulse rate");
        }

        if (RespiratoryRate <= 0)
        {
            yield return PositiveValueError(nameof(RespiratoryRate), "Respiratory rate");
        }

        if (OxygenSaturation <= 0)
        {
            yield return PositiveValueError(nameof(OxygenSaturation), "Oxygen saturation");
        }

        if (HeightCentimeters <= 0)
        {
            yield return PositiveValueError(nameof(HeightCentimeters), "Height");
        }

        if (WeightKilograms <= 0)
        {
            yield return PositiveValueError(nameof(WeightKilograms), "Weight");
        }
    }

    private static ValidationResult PositiveValueError(string memberName, string displayName) =>
        new($"{displayName} must be greater than zero", [memberName]);
}
