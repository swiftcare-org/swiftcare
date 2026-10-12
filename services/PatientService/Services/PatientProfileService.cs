using Microsoft.EntityFrameworkCore;
using PatientService.Data;
using PatientService.Models.Dtos;

namespace PatientService.Services;

public sealed class PatientProfileService : IPatientProfileService
{
    private readonly PatientDbContext _dbContext;

    public PatientProfileService(PatientDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PatientProfileResponse?> GetPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Patients
            .AsNoTracking()
            .Where(p => p.Id == patientId && !p.IsDeleted)
            .Select(p => new PatientProfileResponse
            {
                PatientId = p.Id,
                FullName = p.FullName,
                Nic = p.Nic,
                DateOfBirth = p.DateOfBirth,
                Gender = p.Gender,
                Address = p.Address,
                PhoneNumber = p.PhoneNumber,
                BloodGroup = p.BloodGroup,
                RegisteredAt = p.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PatientProfileResponse?> UpdatePatientAsync(
        Guid patientId,
        UpdatePatientRequest request,
        CancellationToken cancellationToken = default)
    {
        var patient = await _dbContext.Patients
            .Where(p => p.Id == patientId && !p.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if (patient is null)
        {
            return null;
        }

        patient.Address = request.Address.Trim();
        patient.PhoneNumber = request.PhoneNumber.Trim();
        patient.BloodGroup = request.BloodGroup!.Value;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new PatientProfileResponse
        {
            PatientId = patient.Id,
            FullName = patient.FullName,
            Nic = patient.Nic,
            DateOfBirth = patient.DateOfBirth,
            Gender = patient.Gender,
            Address = patient.Address,
            PhoneNumber = patient.PhoneNumber,
            BloodGroup = patient.BloodGroup,
            RegisteredAt = patient.CreatedAt
        };
    }
}
