using MedicalRecordService.Data;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Entities;
using MedicalRecordService.Models.Enums;

namespace MedicalRecordService.Services;

public sealed class ConsultationTemplateService : IConsultationTemplateService
{
    private readonly IConsultationTemplateRepository _templateRepository;
    private readonly TimeProvider _timeProvider;

    public ConsultationTemplateService(
        IConsultationTemplateRepository templateRepository,
        TimeProvider timeProvider)
    {
        _templateRepository = templateRepository;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ConsultationTemplateResponse>> GetTemplatesForDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        RequireDoctorId(doctorId);

        var templates = await _templateRepository.ListVisibleToDoctorAsync(doctorId, cancellationToken);
        return templates.Select(ToResponse).ToList();
    }

    public async Task<CreateTemplateResult> CreateAsync(
        CreateConsultationTemplateRequest request,
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        RequireDoctorId(doctorId);

        // Only the name is trimmed. The clinical text is stored as typed, because a
        // template often ends with a prompt such as "- " for the doctor to continue from.
        var template = new ConsultationTemplate
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Symptoms = request.Symptoms,
            ExaminationFindings = request.ExaminationFindings,
            Notes = request.Notes,
            CreatedByDoctorId = doctorId,
            IsActive = true,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
        };

        var added = await _templateRepository.TryAddAsync(template, cancellationToken);

        return added
            ? new CreateTemplateResult { Outcome = CreateTemplateOutcome.Created, Template = ToResponse(template) }
            : new CreateTemplateResult { Outcome = CreateTemplateOutcome.DuplicateName };
    }

    public async Task<RemoveTemplateOutcome> RemoveAsync(
        Guid templateId,
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        RequireDoctorId(doctorId);

        if (templateId == Guid.Empty)
        {
            throw new ArgumentException("Template ID must be provided.", nameof(templateId));
        }

        var template = await _templateRepository.FindAsync(templateId, cancellationToken);
        if (template is null || !template.IsActive)
        {
            return RemoveTemplateOutcome.NotFound;
        }

        if (template.CreatedByDoctorId is null)
        {
            return RemoveTemplateOutcome.BuiltIn;
        }

        // Another doctor's template is reported as missing, so its existence stays private.
        if (template.CreatedByDoctorId != doctorId)
        {
            return RemoveTemplateOutcome.NotFound;
        }

        var removed = await _templateRepository.DeactivateAsync(templateId, doctorId, cancellationToken);
        return removed ? RemoveTemplateOutcome.Removed : RemoveTemplateOutcome.NotFound;
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
