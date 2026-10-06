using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Services;

public interface IConsultationTemplateService
{
    Task<IReadOnlyList<ConsultationTemplateResponse>> GetTemplatesForDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default);
}
