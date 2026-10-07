using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;

namespace PrescriptionService.Services;

public interface INoPrescriptionService
{
    Task<RecordNoPrescriptionResult> RecordAsync(
        Guid consultationId,
        RecordNoPrescriptionRequest request,
        Guid doctorId,
        string doctorName,
        CancellationToken cancellationToken = default);
}
