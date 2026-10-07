using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Enums;

namespace MedicalRecordService.Services;

public interface IConsultationTemplateService
{
    Task<IReadOnlyList<ConsultationTemplateResponse>> GetTemplatesForDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default);

    Task<CreateTemplateResult> CreateAsync(
        CreateConsultationTemplateRequest request,
        Guid doctorId,
        CancellationToken cancellationToken = default);

    Task<RemoveTemplateOutcome> RemoveAsync(
        Guid templateId,
        Guid doctorId,
        CancellationToken cancellationToken = default);
}
