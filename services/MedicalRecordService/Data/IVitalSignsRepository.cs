using MedicalRecordService.Models.Entities;
using MedicalRecordService.Models.Enums;

namespace MedicalRecordService.Data;

public interface IVitalSignsRepository
{
    Task<VitalSignsPersistenceOutcome> CreateAsync(
        VitalSignsDraft vitalSigns,
        Guid doctorId,
        CancellationToken cancellationToken = default);
}
