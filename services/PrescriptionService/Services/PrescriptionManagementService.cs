using Microsoft.EntityFrameworkCore;
using PrescriptionService.Data;
using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Models.Entities;

namespace PrescriptionService.Services;

public sealed class PrescriptionManagementService(
    PrescriptionDbContext dbContext,
    TimeProvider timeProvider) : IPrescriptionService
{
    public async Task<CreatePrescriptionResult> CreateAsync(
        CreatePrescriptionRequest request,
        Guid doctorId,
        string doctorName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (doctorId == Guid.Empty)
        {
            throw new ArgumentException("Doctor ID must be provided.", nameof(doctorId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(doctorName);

        if (request.Medicines.Count == 0)
        {
            throw new ArgumentException("Add at least one medicine", nameof(request));
        }

        var alreadyExists = await dbContext.Prescriptions
            .AsNoTracking()
            .AnyAsync(
                prescription => prescription.ConsultationId == request.ConsultationId,
                cancellationToken);

        if (alreadyExists)
        {
            return new CreatePrescriptionResult(
                CreatePrescriptionOutcome.ConsultationAlreadyHasPrescription);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var prescription = new Prescription
        {
            Id = Guid.NewGuid(),
            ConsultationId = request.ConsultationId,
            QueueId = request.QueueId,
            PatientId = request.PatientId,
            DoctorId = doctorId,
            DoctorName = doctorName.Trim(),
            Status = Prescription.PendingStatus,
            CreatedAt = now,
            UpdatedAt = now
        };

        for (var index = 0; index < request.Medicines.Count; index++)
        {
            var medicine = request.Medicines[index];
            prescription.Items.Add(new PrescriptionItem
            {
                Id = Guid.NewGuid(),
                ItemOrder = index,
                MedicineName = medicine.MedicineName.Trim(),
                Dosage = medicine.Dosage.Trim(),
                Frequency = medicine.Frequency.Trim(),
                Duration = medicine.Duration.Trim(),
                Instructions = NormalizeOptional(medicine.Instructions)
            });
        }

        dbContext.Prescriptions.Add(prescription);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var duplicateWasPersisted = await dbContext.Prescriptions
                .AsNoTracking()
                .AnyAsync(
                    candidate => candidate.ConsultationId == request.ConsultationId,
                    cancellationToken);

            if (!duplicateWasPersisted)
            {
                throw;
            }

            return new CreatePrescriptionResult(
                CreatePrescriptionOutcome.ConsultationAlreadyHasPrescription);
        }

        return new CreatePrescriptionResult(
            CreatePrescriptionOutcome.Success,
            ToResponse(prescription));
    }

    public async Task<IReadOnlyList<PrescriptionResponse>> GetForPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        if (patientId == Guid.Empty)
        {
            throw new ArgumentException("Patient ID must be provided.", nameof(patientId));
        }

        var prescriptions = await dbContext.Prescriptions
            .AsNoTracking()
            .Include(prescription => prescription.Items)
            .Where(prescription => prescription.PatientId == patientId)
            .OrderByDescending(prescription => prescription.CreatedAt)
            .ThenByDescending(prescription => prescription.Id)
            .ToListAsync(cancellationToken);

        return prescriptions.Select(ToResponse).ToArray();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PrescriptionResponse ToResponse(Prescription prescription) => new(
        prescription.Id,
        prescription.ConsultationId,
        prescription.QueueId,
        prescription.PatientId,
        prescription.DoctorId,
        prescription.DoctorName,
        prescription.Status,
        prescription.CreatedAt,
        prescription.Items
            .OrderBy(item => item.ItemOrder)
            .Select(item => new PrescriptionItemResponse(
                item.Id,
                item.ItemOrder,
                item.MedicineName,
                item.Dosage,
                item.Frequency,
                item.Duration,
                item.Instructions))
            .ToArray());
}
