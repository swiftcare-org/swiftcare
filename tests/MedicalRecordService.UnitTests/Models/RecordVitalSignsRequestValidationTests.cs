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

    [Fact]
    public void EmptyRequestRequiresAtLeastOneVitalSign()
    {
        var result = Assert.Single(Validate(new RecordVitalSignsRequest()));

        Assert.Equal("At least one vital sign is required", result.ErrorMessage);
        Assert.Equal(MeasurementMembers, result.MemberNames);
    }

    [Theory]
    [InlineData(nameof(RecordVitalSignsRequest.TemperatureCelsius))]
    [InlineData(nameof(RecordVitalSignsRequest.PulseRate))]
    [InlineData(nameof(RecordVitalSignsRequest.RespiratoryRate))]
    [InlineData(nameof(RecordVitalSignsRequest.OxygenSaturation))]
    [InlineData(nameof(RecordVitalSignsRequest.HeightCentimeters))]
    [InlineData(nameof(RecordVitalSignsRequest.WeightKilograms))]
    public void AnySingleMeasurementIsEnough(string member)
    {
        var request = new RecordVitalSignsRequest();
        var property = typeof(RecordVitalSignsRequest).GetProperty(member)!;
        property.SetValue(request, property.PropertyType == typeof(decimal?) ? 36.6m : (object)72);

        Assert.Empty(Validate(request));
    }

    [Theory]
    [InlineData(120, null)]
    [InlineData(null, 80)]
    public void BloodPressureNeedsBothValues(int? systolic, int? diastolic)
    {
        var request = new RecordVitalSignsRequest
        {
            SystolicBloodPressure = systolic,
            DiastolicBloodPressure = diastolic
        };

        var result = Assert.Single(Validate(request));

        Assert.Equal("Blood pressure requires both systolic and diastolic values", result.ErrorMessage);
        Assert.Equal(
            [nameof(RecordVitalSignsRequest.SystolicBloodPressure), nameof(RecordVitalSignsRequest.DiastolicBloodPressure)],
            result.MemberNames);
    }

    [Theory]
    [InlineData(nameof(RecordVitalSignsRequest.SystolicBloodPressure), "Systolic blood pressure must be greater than zero")]
    [InlineData(nameof(RecordVitalSignsRequest.DiastolicBloodPressure), "Diastolic blood pressure must be greater than zero")]
    [InlineData(nameof(RecordVitalSignsRequest.TemperatureCelsius), "Temperature must be greater than zero")]
    [InlineData(nameof(RecordVitalSignsRequest.PulseRate), "Pulse rate must be greater than zero")]
    [InlineData(nameof(RecordVitalSignsRequest.RespiratoryRate), "Respiratory rate must be greater than zero")]
    [InlineData(nameof(RecordVitalSignsRequest.OxygenSaturation), "Oxygen saturation must be greater than zero")]
    [InlineData(nameof(RecordVitalSignsRequest.HeightCentimeters), "Height must be greater than zero")]
    [InlineData(nameof(RecordVitalSignsRequest.WeightKilograms), "Weight must be greater than zero")]
    public void ZeroMeasurementNamesTheMeasurementInItsMessage(string member, string message)
    {
        var results = Validate(RequestWithEveryMeasurement(0));

        var result = Assert.Single(results, candidate => candidate.MemberNames.SequenceEqual([member]));
        Assert.Equal(message, result.ErrorMessage);
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
