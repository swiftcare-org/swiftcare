using MedicalRecordService.Models.Entities;

namespace MedicalRecordService.Data;

public interface IConsultationTemplateRepository
{
    // Built-in templates first, then the doctor's own, each ordered by name.
    Task<IReadOnlyList<ConsultationTemplate>> ListVisibleToDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default);
}
