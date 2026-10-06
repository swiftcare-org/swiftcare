using MedicalRecordService.Models.Entities;

namespace MedicalRecordService.Data;

public interface IConsultationTemplateRepository
{
    Task<IReadOnlyList<ConsultationTemplate>> ListActiveAsync(
        CancellationToken cancellationToken = default);
}
