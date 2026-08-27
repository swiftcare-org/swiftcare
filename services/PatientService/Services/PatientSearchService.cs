using Microsoft.EntityFrameworkCore;
using PatientService.Data;
using PatientService.Models.Dtos;

namespace PatientService.Services;

public sealed class PatientSearchService : IPatientSearchService
{
    // A single character matches most of the table and is never a deliberate search, so
    // the shortest useful term is two characters.
    private const int MinimumTermLength = 2;

    // The FullName column ceiling: a longer term cannot match any stored value, so it is
    // rejected before it reaches the database rather than scanning for a guaranteed miss.
    private const int MaximumTermLength = 128;

    private const int MaximumResults = 20;

    private static readonly IReadOnlyList<PatientSearchResultResponse> NoResults = [];

    private readonly PatientDbContext _dbContext;

    public PatientSearchService(PatientDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<PatientSearchResultResponse>> SearchPatientsAsync(
        string? term,
        CancellationToken cancellationToken = default)
    {
        var trimmedTerm = term?.Trim() ?? string.Empty;

        // A term that is absent, too short, or too long is an empty result rather than a
        // validation error: the search box queries as the receptionist types, and a 400 on
        // the first keystroke would surface as an error state for normal typing.
        if (trimmedTerm.Length is < MinimumTermLength or > MaximumTermLength)
        {
            return NoResults;
        }

        // Both sides are lowered in the query itself rather than relying on MySQL's
        // case-insensitive default collation, so case insensitivity is a property of this
        // code and holds on any provider - including the case-sensitive InMemory provider
        // the unit tests run against.
        var loweredTerm = trimmedTerm.ToLowerInvariant();

        // string.Contains is used rather than EF.Functions.Like because EF translates a
        // parameterized Contains through MySQL's LOCATE: a '%' or '_' typed by a
        // receptionist is matched literally instead of becoming a wildcard.
        return await _dbContext.Patients
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Where(p => p.FullName.ToLower().Contains(loweredTerm)
                || p.Nic.ToLower().Contains(loweredTerm)
                // Phone numbers hold only digits and '+', so case folding is meaningless here.
                || p.PhoneNumber.Contains(trimmedTerm))
            .OrderBy(p => p.FullName)
            .ThenBy(p => p.Id)
            .Take(MaximumResults)
            // Projected in the query so the columns a search has no use for are never read
            // out of the database at all.
            .Select(p => new PatientSearchResultResponse
            {
                PatientId = p.Id,
                FullName = p.FullName,
                Nic = p.Nic,
                PhoneNumber = p.PhoneNumber,
                BloodGroup = p.BloodGroup
            })
            .ToListAsync(cancellationToken);
    }
}
