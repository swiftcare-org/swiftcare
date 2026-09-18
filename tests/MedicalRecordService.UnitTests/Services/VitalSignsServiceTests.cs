using MedicalRecordService.Data;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Entities;
using MedicalRecordService.Models.Enums;
using MedicalRecordService.Services;
using Moq;

namespace MedicalRecordService.UnitTests.Services;

public class VitalSignsServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 18, 9, 30, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedUtcNow;
    }

    [Fact]
    public async Task RecordPersistsAllMeasurementsAndCalculatedBmiForConsultation()
    {
        var repository = new Mock<IVitalSignsRepository>();
        VitalSignsDraft? persisted = null;
        var doctorId = Guid.NewGuid();
        var consultationId = Guid.NewGuid();
        repository
            .Setup(repo => repo.CreateAsync(
                It.IsAny<VitalSignsDraft>(),
                doctorId,
                It.IsAny<CancellationToken>()))
            .Callback<VitalSignsDraft, Guid, CancellationToken>((vitalSigns, _, _) =>
                persisted = vitalSigns)
            .ReturnsAsync(VitalSignsPersistenceOutcome.Success);
        var service = new VitalSignsService(repository.Object, new FixedTimeProvider());

        var result = await service.RecordAsync(
            consultationId,
            doctorId,
            new RecordVitalSignsRequest
            {
                SystolicBloodPressure = 118,
                DiastolicBloodPressure = 76,
                TemperatureCelsius = 36.8m,
                PulseRate = 72,
                RespiratoryRate = 16,
                OxygenSaturation = 98,
                HeightCentimeters = 175m,
                WeightKilograms = 70m
            });

        Assert.Equal(RecordVitalSignsOutcome.Success, result.Outcome);
        Assert.NotNull(persisted);
        Assert.Equal(consultationId, persisted.ConsultationId);
        Assert.Equal(118, persisted.SystolicBloodPressure);
        Assert.Equal(76, persisted.DiastolicBloodPressure);
        Assert.Equal(36.8m, persisted.TemperatureCelsius);
        Assert.Equal(72, persisted.PulseRate);
        Assert.Equal(16, persisted.RespiratoryRate);
        Assert.Equal(98, persisted.OxygenSaturation);
        Assert.Equal(175m, persisted.HeightCentimeters);
        Assert.Equal(70m, persisted.WeightKilograms);
        Assert.Equal(22.86m, persisted.Bmi);
        Assert.Equal(FixedUtcNow.UtcDateTime, persisted.RecordedAt);
        Assert.Equal(DateTimeKind.Utc, persisted.RecordedAt.Kind);

        var response = Assert.IsType<VitalSignsResponse>(result.VitalSigns);
        Assert.Equal(persisted.Id, response.Id);
        Assert.Equal(consultationId, response.ConsultationId);
        Assert.Equal(22.86m, response.Bmi);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecordWithoutHeightOrWeightLeavesBmiEmpty(bool includeHeight)
    {
        var repository = new Mock<IVitalSignsRepository>();
        VitalSignsDraft? persisted = null;
        repository
            .Setup(repo => repo.CreateAsync(
                It.IsAny<VitalSignsDraft>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Callback<VitalSignsDraft, Guid, CancellationToken>((vitalSigns, _, _) =>
                persisted = vitalSigns)
            .ReturnsAsync(VitalSignsPersistenceOutcome.Success);
        var service = new VitalSignsService(repository.Object, new FixedTimeProvider());

        var result = await service.RecordAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new RecordVitalSignsRequest
            {
                HeightCentimeters = includeHeight ? 175m : null,
                WeightKilograms = includeHeight ? null : 70m
            });

        Assert.Equal(RecordVitalSignsOutcome.Success, result.Outcome);
        Assert.NotNull(persisted);
        Assert.Null(persisted.Bmi);
        Assert.Null(result.VitalSigns!.Bmi);
    }
}
