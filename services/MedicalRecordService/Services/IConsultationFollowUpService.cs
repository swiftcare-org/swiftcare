using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Services;

public interface IConsultationFollowUpService
{
    Task<OverdueFollowUpResponse?> FindOverdueAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);
}
