using MedicalRecordService.Data;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Entities;
using MedicalRecordService.Models.Enums;

namespace MedicalRecordService.Services;

public sealed class VitalSignsService : IVitalSignsService
{
    private readonly IVitalSignsRepository _vitalSignsRepository;
    private readonly TimeProvider _timeProvider;

    public VitalSignsService(
        IVitalSignsRepository vitalSignsRepository,
        TimeProvider timeProvider)
    {
        _vitalSignsRepository = vitalSignsRepository;
        _timeProvider = timeProvider;
    }

    public async Task<RecordVitalSignsResult> RecordAsync(
        Guid consultationId,
        Guid doctorId,
        RecordVitalSignsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (consultationId == Guid.Empty)
        {
            throw new ArgumentException("Consultation ID must be provided.", nameof(consultationId));
        }

        if (doctorId == Guid.Empty)
        {
            throw new ArgumentException("Doctor ID must be provided.", nameof(doctorId));
        }

        ArgumentNullException.ThrowIfNull(request);

        var heightCentimeters = RoundOptional(request.HeightCentimeters, 2);
        var weightKilograms = RoundOptional(request.WeightKilograms, 2);
        var vitalSigns = new VitalSignsDraft
        {
            Id = Guid.NewGuid(),
            ConsultationId = consultationId,
            SystolicBloodPressure = request.SystolicBloodPressure,
            DiastolicBloodPressure = request.DiastolicBloodPressure,
            TemperatureCelsius = RoundOptional(request.TemperatureCelsius, 1),
            PulseRate = request.PulseRate,
            RespiratoryRate = request.RespiratoryRate,
            OxygenSaturation = request.OxygenSaturation,
            HeightCentimeters = heightCentimeters,
            WeightKilograms = weightKilograms,
            Bmi = CalculateBmi(heightCentimeters, weightKilograms),
            RecordedAt = DateTime.SpecifyKind(
                _timeProvider.GetUtcNow().UtcDateTime,
                DateTimeKind.Utc)
        };

        var outcome = await _vitalSignsRepository.CreateAsync(
            vitalSigns,
            doctorId,
            cancellationToken);

        return outcome switch
        {
            VitalSignsPersistenceOutcome.Success => new RecordVitalSignsResult
            {
                Outcome = RecordVitalSignsOutcome.Success,
                VitalSigns = ToResponse(vitalSigns)
            },
            VitalSignsPersistenceOutcome.ConsultationNotFound => new RecordVitalSignsResult
            {
                Outcome = RecordVitalSignsOutcome.ConsultationNotFound
            },
            VitalSignsPersistenceOutcome.VitalSignsAlreadyExist => new RecordVitalSignsResult
            {
                Outcome = RecordVitalSignsOutcome.VitalSignsAlreadyExist
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "Unsupported vital-signs persistence outcome.")
        };
    }

    private static decimal? CalculateBmi(decimal? heightCentimeters, decimal? weightKilograms)
    {
        if (!heightCentimeters.HasValue || !weightKilograms.HasValue)
        {
            return null;
        }

        var heightMeters = heightCentimeters.Value / 100m;
        return Math.Round(
            weightKilograms.Value / (heightMeters * heightMeters),
            2,
            MidpointRounding.AwayFromZero);
    }

    private static decimal? RoundOptional(decimal? value, int decimalPlaces) =>
        value.HasValue
            ? Math.Round(value.Value, decimalPlaces, MidpointRounding.AwayFromZero)
            : null;

    private static VitalSignsResponse ToResponse(VitalSignsDraft vitalSigns) => new()
    {
        Id = vitalSigns.Id,
        ConsultationId = vitalSigns.ConsultationId,
        SystolicBloodPressure = vitalSigns.SystolicBloodPressure,
        DiastolicBloodPressure = vitalSigns.DiastolicBloodPressure,
        TemperatureCelsius = vitalSigns.TemperatureCelsius,
        PulseRate = vitalSigns.PulseRate,
        RespiratoryRate = vitalSigns.RespiratoryRate,
        OxygenSaturation = vitalSigns.OxygenSaturation,
        HeightCentimeters = vitalSigns.HeightCentimeters,
        WeightKilograms = vitalSigns.WeightKilograms,
        Bmi = vitalSigns.Bmi,
        RecordedAt = vitalSigns.RecordedAt
    };
}
