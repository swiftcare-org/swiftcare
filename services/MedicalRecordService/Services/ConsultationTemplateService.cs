using MedicalRecordService.Data;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Entities;

namespace MedicalRecordService.Services;

public sealed class ConsultationTemplateService : IConsultationTemplateService
{
    private readonly IConsultationTemplateRepository _templateRepository;

    public ConsultationTemplateService(IConsultationTemplateRepository templateRepository)
    {
        _templateRepository = templateRepository;
    }

    public async Task<IReadOnlyList<ConsultationTemplateResponse>> GetTemplatesForDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        RequireDoctorId(doctorId);

        var templates = await _templateRepository.ListVisibleToDoctorAsync(doctorId, cancellationToken);
        return templates.Select(ToResponse).ToList();
    }

    private static void RequireDoctorId(Guid doctorId)
    {
        if (doctorId == Guid.Empty)
        {
            throw new ArgumentException("Doctor ID must be provided.", nameof(doctorId));
        }
    }

    private static ConsultationTemplateResponse ToResponse(ConsultationTemplate template) => new()
    {
        Id = template.Id,
        Name = template.Name,
        Symptoms = template.Symptoms,
        ExaminationFindings = template.ExaminationFindings,
        Notes = template.Notes,
        IsBuiltIn = template.CreatedByDoctorId is null
    };
}
