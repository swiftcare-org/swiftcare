using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Entities;

namespace MedicalRecordService.Data;

public interface IConsultationRepository
{
    Task<ConsultationPersistenceResult> CreateAsync(
        ConsultationDraft consultation,
        CancellationToken cancellationToken = default);
}
