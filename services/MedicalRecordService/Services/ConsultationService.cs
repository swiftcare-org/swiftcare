using MedicalRecordService.Data;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Entities;
using MedicalRecordService.Models.Enums;

namespace MedicalRecordService.Services;

public sealed class ConsultationService : IConsultationService
{
    private readonly IConsultationRepository _consultationRepository;
    private readonly TimeProvider _timeProvider;

    public ConsultationService(
        IConsultationRepository consultationRepository,
        TimeProvider timeProvider)
    {
        _consultationRepository = consultationRepository;
        _timeProvider = timeProvider;
    }

    public async Task<CreateConsultationResult> CreateAsync(
        CreateConsultationRequest request,
        Guid doctorId,
        string doctorName,
        string roomNumber,
        CancellationToken cancellationToken = default)
    {
        if (doctorId == Guid.Empty)
        {
            throw new ArgumentException("Doctor ID must be provided.", nameof(doctorId));
        }

        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(doctorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(roomNumber);

        var consultationDate = DateTime.SpecifyKind(
            _timeProvider.GetUtcNow().UtcDateTime,
            DateTimeKind.Utc);
        var consultation = new ConsultationDraft
        {
            Id = Guid.NewGuid(),
            QueueId = request.QueueId,
            PatientId = request.PatientId,
            DoctorId = doctorId,
            DoctorName = doctorName.Trim(),
            RoomNumber = roomNumber.Trim(),
            Symptoms = request.Symptoms.Trim(),
            ExaminationFindings = NormalizeOptional(request.ExaminationFindings),
            Diagnosis = request.Diagnosis.Trim(),
            Notes = NormalizeOptional(request.Notes),
            FollowUpDate = request.FollowUpDate,
            FollowUpInstructions = NormalizeOptional(request.FollowUpInstructions),
            TemplateId = request.TemplateId,
            ConsultationDate = consultationDate
        };

        var persistenceResult = await _consultationRepository.CreateAsync(
            consultation,
            cancellationToken);

        return persistenceResult.Outcome switch
        {
            ConsultationPersistenceOutcome.Success => new CreateConsultationResult
            {
                Outcome = CreateConsultationOutcome.Success,
                Consultation = ToResponse(consultation, persistenceResult.TemplateName)
            },
            ConsultationPersistenceOutcome.TemplateNotFound => new CreateConsultationResult
            {
                Outcome = CreateConsultationOutcome.TemplateNotFound
            },
            ConsultationPersistenceOutcome.QueueAlreadyHasConsultation => new CreateConsultationResult
            {
                Outcome = CreateConsultationOutcome.QueueAlreadyHasConsultation
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(persistenceResult),
                persistenceResult.Outcome,
                "Unsupported consultation persistence outcome.")
        };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ConsultationResponse ToResponse(
        ConsultationDraft consultation,
        string? templateName) => new()
        {
            Id = consultation.Id,
            QueueId = consultation.QueueId,
            PatientId = consultation.PatientId,
            DoctorId = consultation.DoctorId,
            DoctorName = consultation.DoctorName,
            RoomNumber = consultation.RoomNumber,
            Symptoms = consultation.Symptoms,
            ExaminationFindings = consultation.ExaminationFindings,
            Diagnosis = consultation.Diagnosis,
            Notes = consultation.Notes,
            FollowUpDate = consultation.FollowUpDate,
            FollowUpInstructions = consultation.FollowUpInstructions,
            TemplateId = consultation.TemplateId,
            TemplateName = templateName,
            ConsultationDate = consultation.ConsultationDate
        };
}
