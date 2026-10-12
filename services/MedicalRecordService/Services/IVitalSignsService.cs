using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Services;

public interface IVitalSignsService
{
    Task<RecordVitalSignsResult> RecordAsync(
        Guid consultationId,
        Guid doctorId,
        RecordVitalSignsRequest request,
        CancellationToken cancellationToken = default);
}
