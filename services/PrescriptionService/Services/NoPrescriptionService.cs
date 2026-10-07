using Microsoft.EntityFrameworkCore;
using PrescriptionService.Data;
using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Models.Entities;

namespace PrescriptionService.Services;

public sealed class NoPrescriptionService(
    PrescriptionDbContext dbContext,
    TimeProvider timeProvider) : INoPrescriptionService
{
    public async Task<RecordNoPrescriptionResult> RecordAsync(
        Guid consultationId,
        RecordNoPrescriptionRequest request,
        Guid doctorId,
        string doctorName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (consultationId == Guid.Empty)
        {
            throw new ArgumentException("Consultation ID must be provided.", nameof(consultationId));
        }

        if (doctorId == Guid.Empty)
        {
            throw new ArgumentException("Doctor ID must be provided.", nameof(doctorId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(doctorName);

        // A consultation ends with a prescription or with this decision, never both.
        if (await FindConflictAsync(consultationId, cancellationToken) is { } conflict)
        {
            return new RecordNoPrescriptionResult(conflict);
        }

        var decision = new NoPrescriptionDecision
        {
            Id = Guid.NewGuid(),
            ConsultationId = consultationId,
            QueueId = request.QueueId,
            PatientId = request.PatientId,
            DoctorId = doctorId,
            DoctorName = doctorName.Trim(),
            RecordedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        dbContext.NoPrescriptionDecisions.Add(decision);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A matching request can commit between the check above and this save. The
            // unique index on ConsultationId rejects the second one.
            if (await FindConflictAsync(consultationId, cancellationToken) is not { } lateConflict)
            {
                throw;
            }

            return new RecordNoPrescriptionResult(lateConflict);
        }

        return new RecordNoPrescriptionResult(
            RecordNoPrescriptionOutcome.Recorded,
            NoPrescriptionResponses.From(decision));
    }

    private async Task<RecordNoPrescriptionOutcome?> FindConflictAsync(
        Guid consultationId,
        CancellationToken cancellationToken)
    {
        var hasPrescription = await dbContext.Prescriptions
            .AsNoTracking()
            .AnyAsync(prescription => prescription.ConsultationId == consultationId, cancellationToken);
        if (hasPrescription)
        {
            return RecordNoPrescriptionOutcome.ConsultationAlreadyHasPrescription;
        }

        var alreadyRecorded = await dbContext.NoPrescriptionDecisions
            .AsNoTracking()
            .AnyAsync(decision => decision.ConsultationId == consultationId, cancellationToken);

        return alreadyRecorded ? RecordNoPrescriptionOutcome.AlreadyRecorded : null;
    }
}
