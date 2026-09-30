using MedicalRecordService.Data;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Entities;

namespace MedicalRecordService.Services;

public sealed class ConsultationHistoryService : IConsultationHistoryService
{
    private readonly IConsultationHistoryRepository _repository;

    public ConsultationHistoryService(IConsultationHistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ConsultationResponse>> GetHistoryAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        EnsurePatientId(patientId);

        var consultations = await _repository.ListCompletedAsync(patientId, cancellationToken);
        return consultations.Select(ToResponse).ToList();
    }

    public async Task<ConsultationResponse?> GetLatestAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        EnsurePatientId(patientId);

        var consultation = await _repository.FindLatestCompletedAsync(patientId, cancellationToken);
        return consultation is null ? null : ToResponse(consultation);
    }

    private static void EnsurePatientId(Guid patientId)
    {
        if (patientId == Guid.Empty)
        {
            throw new ArgumentException("Patient ID must be provided.", nameof(patientId));
        }
    }

    private static ConsultationResponse ToResponse(Consultation consultation) => new()
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
        TemplateName = consultation.TemplateName,
        ConsultationDate = consultation.ConsultationDate
    };
}
