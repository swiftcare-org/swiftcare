using MedicalRecordService.Models.Entities;

namespace MedicalRecordService.Data;

public interface IVitalSignsHistoryRepository
{
    Task<IReadOnlyList<VitalSigns>> ListForPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);
}
