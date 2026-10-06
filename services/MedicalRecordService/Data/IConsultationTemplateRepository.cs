using MedicalRecordService.Models.Entities;

namespace MedicalRecordService.Data;

public interface IConsultationTemplateRepository
{
    // Built-in templates first, then the doctor's own, each ordered by name.
    Task<IReadOnlyList<ConsultationTemplate>> ListVisibleToDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default);

    // False when the owner already has an active template with this name. The database
    // decides, so two requests arriving together cannot both succeed.
    Task<bool> TryAddAsync(
        ConsultationTemplate template,
        CancellationToken cancellationToken = default);
}
