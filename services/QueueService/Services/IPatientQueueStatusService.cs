using QueueService.Models.Dtos;

namespace QueueService.Services;

public interface IPatientQueueStatusService
{
    Task<PatientQueueStatusResponse> GetTodayStatusAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);
}
