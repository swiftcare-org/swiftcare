using PatientService.Models.Dtos;

namespace PatientService.Services;

public interface IPatientSearchService
{
    Task<IReadOnlyList<PatientSearchResultResponse>> SearchPatientsAsync(
        string? term,
        CancellationToken cancellationToken = default);
}
