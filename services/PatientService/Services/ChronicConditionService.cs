using Microsoft.EntityFrameworkCore;
using PatientService.Data;
using PatientService.Models.Dtos;
using PatientService.Models.Entities;

namespace PatientService.Services;

public sealed class ChronicConditionService : IChronicConditionService
{
    private readonly PatientDbContext _dbContext;
    private readonly ILogger<ChronicConditionService> _logger;

    public ChronicConditionService(
        PatientDbContext dbContext,
        ILogger<ChronicConditionService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ChronicConditionResponse>?> GetConditionsAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        if (!await PatientExistsAsync(patientId, cancellationToken))
        {
            return null;
        }

        return await _dbContext.ChronicConditions
            .AsNoTracking()
            .Where(condition => condition.PatientId == patientId && !condition.IsDeleted)
            .OrderByDescending(condition => condition.DateDiagnosed)
            .ThenBy(condition => condition.ConditionName)
            .Select(condition => new ChronicConditionResponse
            {
                ConditionId = condition.Id,
                ConditionName = condition.ConditionName,
                DateDiagnosed = condition.DateDiagnosed,
                Notes = condition.Notes
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ChronicConditionResponse?> AddConditionAsync(
        Guid patientId,
        ChronicConditionRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (!await PatientExistsAsync(patientId, cancellationToken))
        {
            return null;
        }

        var condition = new ChronicCondition
        {
            PatientId = patientId,
            ConditionName = request.ConditionName.Trim(),
            DateDiagnosed = request.DateDiagnosed!.Value,
            Notes = NormalizeNotes(request.Notes)
        };

        _dbContext.ChronicConditions.Add(condition);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Chronic condition recorded: patientId={PatientId} conditionId={ConditionId} by userId={UserId}",
            patientId,
            condition.Id,
            actingUserId);

        return ToResponse(condition);
    }

    public async Task<bool> RemoveConditionAsync(
        Guid patientId,
        Guid conditionId,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var condition = await _dbContext.ChronicConditions
            .FirstOrDefaultAsync(
                candidate => candidate.Id == conditionId &&
                    candidate.PatientId == patientId &&
                    !candidate.IsDeleted,
                cancellationToken);

        if (condition is null)
        {
            return false;
        }

        condition.IsDeleted = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Chronic condition removed: patientId={PatientId} conditionId={ConditionId} by userId={UserId}",
            patientId,
            conditionId,
            actingUserId);

        return true;
    }

    private Task<bool> PatientExistsAsync(Guid patientId, CancellationToken cancellationToken) =>
        _dbContext.Patients.AnyAsync(
            patient => patient.Id == patientId && !patient.IsDeleted,
            cancellationToken);

    private static string? NormalizeNotes(string? notes)
    {
        var trimmed = notes?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static ChronicConditionResponse ToResponse(ChronicCondition condition) => new()
    {
        ConditionId = condition.Id,
        ConditionName = condition.ConditionName,
        DateDiagnosed = condition.DateDiagnosed,
        Notes = condition.Notes
    };
}
