using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;

namespace PrescriptionService.Services;

public interface IPrescriptionService
{
    Task<CreatePrescriptionResult> CreateAsync(
        CreatePrescriptionRequest request,
        Guid doctorId,
        string doctorName,
        CancellationToken cancellationToken = default);
}
