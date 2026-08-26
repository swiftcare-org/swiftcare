using Microsoft.EntityFrameworkCore;
using PatientService.Data;
using PatientService.Models.Dtos;
using PatientService.Models.Entities;
using PatientService.Models.Enums;

namespace PatientService.Services;

public sealed class PatientRegistrationService : IPatientRegistrationService
{
    private readonly PatientDbContext _dbContext;
    private readonly IPatientEventPublisher _eventPublisher;
    private readonly ILogger<PatientRegistrationService> _logger;

    public PatientRegistrationService(
        PatientDbContext dbContext,
        IPatientEventPublisher eventPublisher,
        ILogger<PatientRegistrationService> logger)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<RegisterPatientResult> RegisterPatientAsync(
        RegisterPatientRequest request,
        string correlationId,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var normalizedNic = request.Nic.Trim().ToUpperInvariant();

        // Deliberately not filtered by !IsDeleted: the unique index on Nic has no filter,
        // so a collision with a soft-deleted patient must be caught here rather than
        // surfacing as a DbUpdateException at SaveChangesAsync. A soft-deleted patient
        // permanently keeps its NIC for healthcare identity integrity.
        var nicExists = await _dbContext.Patients
            .AnyAsync(p => p.Nic == normalizedNic, cancellationToken);

        if (nicExists)
        {
            LogRejection(RegisterPatientOutcome.DuplicateNic, actingUserId);
            return new RegisterPatientResult { Outcome = RegisterPatientOutcome.DuplicateNic };
        }

        var patient = new Patient
        {
            Nic = normalizedNic,
            FullName = request.FullName.Trim(),
            DateOfBirth = request.DateOfBirth!.Value,
            Gender = request.Gender!.Value,
            Address = request.Address.Trim(),
            PhoneNumber = request.PhoneNumber.Trim(),
            BloodGroup = request.BloodGroup!.Value
        };

        _dbContext.Patients.Add(patient);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two receptionists submitting the same NIC concurrently can both pass the
            // AnyAsync check above; the unique index is the final backstop.
            LogRejection(RegisterPatientOutcome.DuplicateNic, actingUserId);
            return new RegisterPatientResult { Outcome = RegisterPatientOutcome.DuplicateNic };
        }

        // A failed publish is logged but does not fail the request: the patient record is
        // the source of truth and already committed. The patient exists but was never
        // queued in this case - reconciliation via a transactional outbox is a future story.
        var published = await _eventPublisher.PublishPatientCheckedInAsync(
            patient.Id, isNewPatient: true, correlationId, cancellationToken);

        if (!published)
        {
            _logger.LogError(
                "patient-checked-in event failed to publish after patient was persisted: patientId={PatientId} correlationId={CorrelationId}",
                patient.Id,
                correlationId);
        }

        _logger.LogInformation(
            "Patient registered: patientId={PatientId} by userId={UserId}",
            patient.Id,
            actingUserId);

        return new RegisterPatientResult
        {
            Outcome = RegisterPatientOutcome.Success,
            Patient = new RegisteredPatientResponse
            {
                PatientId = patient.Id,
                CreatedAt = patient.CreatedAt
            }
        };
    }

    private void LogRejection(RegisterPatientOutcome outcome, Guid actingUserId)
    {
        _logger.LogInformation(
            "Patient registration rejected: outcome={Outcome} by userId={UserId}",
            outcome,
            actingUserId);
    }
}
