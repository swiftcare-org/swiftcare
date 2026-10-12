using Microsoft.EntityFrameworkCore;
using PatientService.Data;
using PatientService.Models.Enums;

namespace PatientService.Services;

public sealed class PatientCheckInService : IPatientCheckInService
{
    private readonly PatientDbContext _dbContext;
    private readonly IPatientEventPublisher _eventPublisher;

    public PatientCheckInService(
        PatientDbContext dbContext,
        IPatientEventPublisher eventPublisher)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
    }

    public async Task<CheckInPatientOutcome> CheckInPatientAsync(
        Guid patientId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var patientExists = await _dbContext.Patients
            .AsNoTracking()
            .AnyAsync(
                patient => patient.Id == patientId && !patient.IsDeleted,
                cancellationToken);

        if (!patientExists)
        {
            return CheckInPatientOutcome.PatientNotFound;
        }

        var published = await _eventPublisher.PublishPatientCheckedInAsync(
            patientId,
            isNewPatient: false,
            correlationId,
            cancellationToken);

        return published
            ? CheckInPatientOutcome.Success
            : CheckInPatientOutcome.EventPublishFailed;
    }
}
