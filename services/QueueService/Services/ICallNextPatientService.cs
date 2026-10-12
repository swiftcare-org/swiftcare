using QueueService.Models.Dtos;

namespace QueueService.Services;

public interface ICallNextPatientService
{
    Task<CallNextPatientResult> CallNextAsync(
        Guid doctorId,
        string doctorName,
        string roomNumber,
        string correlationId,
        CancellationToken cancellationToken = default);
}
