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

    // Returns the template whether or not it is still active, or null when the ID is unknown.
    Task<ConsultationTemplate?> FindAsync(
        Guid templateId,
        CancellationToken cancellationToken = default);

    // Marks the doctor's own active template inactive. The row is kept, because past
    // consultations still refer to it. False when nothing was changed.
    Task<bool> DeactivateAsync(
        Guid templateId,
        Guid doctorId,
        CancellationToken cancellationToken = default);
}
