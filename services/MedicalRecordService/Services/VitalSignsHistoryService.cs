using MedicalRecordService.Data;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Entities;

namespace MedicalRecordService.Services;

public sealed class VitalSignsHistoryService : IVitalSignsHistoryService
{
    private readonly IVitalSignsHistoryRepository _repository;

    public VitalSignsHistoryService(IVitalSignsHistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<VitalSignsResponse>> GetHistoryAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        if (patientId == Guid.Empty)
        {
            throw new ArgumentException("Patient ID must be provided.", nameof(patientId));
        }

        var readings = await _repository.ListForPatientAsync(patientId, cancellationToken);
        // Newest first is part of the contract, so it is enforced here rather than left to
        // whichever order the repository happens to return.
        return readings
            .OrderByDescending(reading => reading.RecordedAt)
            .Select(ToResponse)
            .ToList();
    }

    private static VitalSignsResponse ToResponse(VitalSigns vitalSigns) => new()
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
