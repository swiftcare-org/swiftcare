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

    public async Task<IReadOnlyList<ConsultationTemplateResponse>> GetActiveTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        var templates = await _templateRepository.ListActiveAsync(cancellationToken);
        return templates.Select(ToResponse).ToList();
    }

    private static ConsultationTemplateResponse ToResponse(ConsultationTemplate template) => new()
    {
        Id = template.Id,
        Name = template.Name,
        Symptoms = template.Symptoms,
        ExaminationFindings = template.ExaminationFindings,
        Notes = template.Notes
    };
}
