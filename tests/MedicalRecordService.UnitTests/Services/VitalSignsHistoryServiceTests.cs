using MedicalRecordService.Data;
using MedicalRecordService.Models.Entities;
using MedicalRecordService.Services;
using Moq;

namespace MedicalRecordService.UnitTests.Services;

public class VitalSignsHistoryServiceTests
{
    [Fact]
    public async Task GetHistoryMapsEveryReadingInRepositoryOrder()
    {
        var patientId = Guid.NewGuid();
        var newest = CreateReading(new DateTime(2026, 9, 21, 4, 0, 0, DateTimeKind.Utc));
        var oldest = CreateReading(new DateTime(2026, 9, 10, 4, 0, 0, DateTimeKind.Utc));
        var repository = new Mock<IVitalSignsHistoryRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.ListForPatientAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([newest, oldest]);

        var result = await new VitalSignsHistoryService(repository.Object).GetHistoryAsync(patientId);

        Assert.Equal([newest.Id, oldest.Id], result.Select(item => item.Id));
        var first = result[0];
        Assert.Equal(newest.ConsultationId, first.ConsultationId);
        Assert.Equal(newest.SystolicBloodPressure, first.SystolicBloodPressure);
        Assert.Equal(newest.DiastolicBloodPressure, first.DiastolicBloodPressure);
        Assert.Equal(newest.TemperatureCelsius, first.TemperatureCelsius);
        Assert.Equal(newest.PulseRate, first.PulseRate);
        Assert.Equal(newest.RespiratoryRate, first.RespiratoryRate);
        Assert.Equal(newest.OxygenSaturation, first.OxygenSaturation);
        Assert.Equal(newest.HeightCentimeters, first.HeightCentimeters);
        Assert.Equal(newest.WeightKilograms, first.WeightKilograms);
        Assert.Equal(newest.Bmi, first.Bmi);
        Assert.Equal(newest.RecordedAt, first.RecordedAt);
        repository.VerifyAll();
    }

    [Fact]
    public async Task GetHistoryKeepsUnrecordedMeasurementsAsNull()
    {
        var patientId = Guid.NewGuid();
        var partial = new VitalSigns
        {
            Id = Guid.NewGuid(),
            ConsultationId = Guid.NewGuid(),
            PulseRate = 72,
            RecordedAt = new DateTime(2026, 9, 21, 4, 0, 0, DateTimeKind.Utc)
        };
        var repository = new Mock<IVitalSignsHistoryRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.ListForPatientAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([partial]);

        var result = await new VitalSignsHistoryService(repository.Object).GetHistoryAsync(patientId);

        var reading = Assert.Single(result);
        Assert.Equal(72, reading.PulseRate);
        Assert.Null(reading.SystolicBloodPressure);
        Assert.Null(reading.TemperatureCelsius);
        Assert.Null(reading.Bmi);
        repository.VerifyAll();
    }

    [Fact]
    public async Task GetHistoryReturnsEmptyListWhenNoVitalsWereRecorded()
    {
        var patientId = Guid.NewGuid();
        var repository = new Mock<IVitalSignsHistoryRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.ListForPatientAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await new VitalSignsHistoryService(repository.Object).GetHistoryAsync(patientId);

        Assert.Empty(result);
        repository.VerifyAll();
    }

    [Fact]
    public async Task EmptyPatientIdIsRejectedWithoutQueryingRepository()
    {
        var repository = new Mock<IVitalSignsHistoryRepository>(MockBehavior.Strict);

        var action = () => new VitalSignsHistoryService(repository.Object).GetHistoryAsync(Guid.Empty);

        await Assert.ThrowsAsync<ArgumentException>(action);
        repository.VerifyNoOtherCalls();
    }

    private static VitalSigns CreateReading(DateTime recordedAt) => new()
    {
        Id = Guid.NewGuid(),
        ConsultationId = Guid.NewGuid(),
        SystolicBloodPressure = 128,
        DiastolicBloodPressure = 82,
        TemperatureCelsius = 36.8m,
        PulseRate = 74,
        RespiratoryRate = 16,
        OxygenSaturation = 98,
        HeightCentimeters = 172.5m,
        WeightKilograms = 70.4m,
        Bmi = 23.66m,
        RecordedAt = recordedAt
    };
}
