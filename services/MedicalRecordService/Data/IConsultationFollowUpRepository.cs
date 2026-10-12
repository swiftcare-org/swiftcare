using MedicalRecordService.Models.Entities;

namespace MedicalRecordService.Data;

public interface IConsultationFollowUpRepository
{
    Task<ConsultationFollowUp?> FindLatestCompletedAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);
}
