using Microsoft.EntityFrameworkCore;
using PatientService.Data;
using PatientService.Models.Dtos;
using PatientService.Models.Entities;
using PatientService.Models.Enums;

namespace PatientService.Services;

public sealed class AllergyService : IAllergyService
{
    private readonly PatientDbContext _dbContext;
    private readonly ILogger<AllergyService> _logger;

    public AllergyService(PatientDbContext dbContext, ILogger<AllergyService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AllergyResponse>?> GetAllergiesAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        if (!await PatientExistsAsync(patientId, cancellationToken))
        {
            return null;
        }

        var allergies = await _dbContext.Allergies
            .AsNoTracking()
            .Where(a => a.PatientId == patientId && !a.IsDeleted)
            .Select(a => new AllergyResponse
            {
                AllergyId = a.Id,
                AllergyName = a.AllergyName,
                Severity = a.Severity,
                Notes = a.Notes,
                RecordedAt = a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        // Ranked in memory rather than via SQL ORDER BY: Severity is persisted as a string
        // (HasConversion<string>() in PatientDbContext), so a database sort yields
        // Mild, Moderate, Severe - exactly backwards from Scenario 3's "Severe first".
        // A patient's allergy list is small by nature, so materializing before sorting
        // costs nothing here.
        return allergies
            .OrderBy(a => SeverityRank(a.Severity))
            .ThenByDescending(a => a.RecordedAt)
            .ToList();
    }

    public async Task<AllergyResponse?> AddAllergyAsync(
        Guid patientId,
        AllergyRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (!await PatientExistsAsync(patientId, cancellationToken))
        {
            return null;
        }

        var allergy = new Allergy
        {
            PatientId = patientId,
            AllergyName = request.AllergyName.Trim(),
            Severity = request.Severity!.Value,
            Notes = NormalizeNotes(request.Notes)
        };

        _dbContext.Allergies.Add(allergy);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Allergy recorded: patientId={PatientId} allergyId={AllergyId} by userId={UserId}",
            patientId,
            allergy.Id,
            actingUserId);

        return ToResponse(allergy);
    }

    public async Task<AllergyResponse?> UpdateAllergyAsync(
        Guid patientId,
        Guid allergyId,
        AllergyRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        // Filtered by patientId, not just allergyId: an allergy id that exists but belongs
        // to a different patient must 404 here, exactly as if it didn't exist, so a
        // receptionist on one patient's profile can never mutate another patient's record.
        var allergy = await _dbContext.Allergies
            .FirstOrDefaultAsync(a => a.Id == allergyId && a.PatientId == patientId && !a.IsDeleted, cancellationToken);

        if (allergy is null)
        {
            return null;
        }

        allergy.AllergyName = request.AllergyName.Trim();
        allergy.Severity = request.Severity!.Value;
        allergy.Notes = NormalizeNotes(request.Notes);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Allergy updated: patientId={PatientId} allergyId={AllergyId} by userId={UserId}",
            patientId,
            allergyId,
            actingUserId);

        return ToResponse(allergy);
    }

    public async Task<bool> RemoveAllergyAsync(
        Guid patientId,
        Guid allergyId,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var allergy = await _dbContext.Allergies
            .FirstOrDefaultAsync(a => a.Id == allergyId && a.PatientId == patientId && !a.IsDeleted, cancellationToken);

        if (allergy is null)
        {
            return false;
        }

        allergy.IsDeleted = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Allergy removed: patientId={PatientId} allergyId={AllergyId} by userId={UserId}",
            patientId,
            allergyId,
            actingUserId);

        return true;
    }

    private Task<bool> PatientExistsAsync(Guid patientId, CancellationToken cancellationToken) =>
        _dbContext.Patients.AnyAsync(p => p.Id == patientId && !p.IsDeleted, cancellationToken);

    // An empty or whitespace-only Notes value is stored as null, so the UI has one empty
    // representation to render rather than two.
    private static string? NormalizeNotes(string? notes)
    {
        var trimmed = notes?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static int SeverityRank(AllergySeverity severity) => severity switch
    {
        AllergySeverity.Severe => 0,
        AllergySeverity.Moderate => 1,
        AllergySeverity.Mild => 2,
        _ => int.MaxValue
    };

    private static AllergyResponse ToResponse(Allergy allergy) => new()
    {
        AllergyId = allergy.Id,
        AllergyName = allergy.AllergyName,
        Severity = allergy.Severity,
        Notes = allergy.Notes,
        RecordedAt = allergy.CreatedAt
    };
}
