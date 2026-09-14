using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Services;

public interface IConsultationTemplateService
{
    Task<IReadOnlyList<ConsultationTemplateResponse>> GetActiveTemplatesAsync(
        CancellationToken cancellationToken = default);
}
