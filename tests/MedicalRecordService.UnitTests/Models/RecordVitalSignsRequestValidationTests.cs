using System.ComponentModel.DataAnnotations;
using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.UnitTests.Models;

public class RecordVitalSignsRequestValidationTests
{
    private static readonly string[] MeasurementMembers =
    [
        nameof(RecordVitalSignsRequest.SystolicBloodPressure),
        nameof(RecordVitalSignsRequest.DiastolicBloodPressure),
        nameof(RecordVitalSignsRequest.TemperatureCelsius),
        nameof(RecordVitalSignsRequest.PulseRate),
        nameof(RecordVitalSignsRequest.RespiratoryRate),
        nameof(RecordVitalSignsRequest.OxygenSaturation),
        nameof(RecordVitalSignsRequest.HeightCentimeters),
        nameof(RecordVitalSignsRequest.WeightKilograms)
    ];

    [Fact]
    public void UnusualPositiveMeasurementsPassValidation()
    {
        var request = new RecordVitalSignsRequest
        {
            SystolicBloodPressure = 180,
            DiastolicBloodPressure = 110,
            TemperatureCelsius = 50m,
            PulseRate = 300,
            RespiratoryRate = 35,
            OxygenSaturation = 80,
            HeightCentimeters = 230m,
            WeightKilograms = 250m
        };

        var results = Validate(request);

        Assert.Empty(results);
    }

    [Fact]
    public void ZeroMeasurementsAreRejected()
    {
        var request = RequestWithEveryMeasurement(0);

        var results = Validate(request);

        AssertEveryMeasurementHasValidationError(results);
    }

    [Fact]
    public void NegativeMeasurementsAreRejected()
    {
        var request = RequestWithEveryMeasurement(-1);

        var results = Validate(request);

        AssertEveryMeasurementHasValidationError(results);
    }

    private static RecordVitalSignsRequest RequestWithEveryMeasurement(int value) => new()
    {
        SystolicBloodPressure = value,
        DiastolicBloodPressure = value,
        TemperatureCelsius = value,
        PulseRate = value,
        RespiratoryRate = value,
        OxygenSaturation = value,
        HeightCentimeters = value,
        WeightKilograms = value
    };

    private static IList<ValidationResult> Validate(RecordVitalSignsRequest request)
    {
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, context, results, validateAllProperties: true);
        return results;
    }

    private static void AssertEveryMeasurementHasValidationError(
        IEnumerable<ValidationResult> results)
    {
        var invalidMembers = results
            .SelectMany(result => result.MemberNames)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(MeasurementMembers, member => Assert.Contains(member, invalidMembers));
    }
}
