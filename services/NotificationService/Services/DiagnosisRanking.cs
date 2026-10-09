using NotificationService.Models.Dtos;

namespace NotificationService.Services;

public static class DiagnosisRanking
{
    // The most common diagnoses, highest first. "Viral URTI" and "viral urti" are one
    // diagnosis. Equal counts are ordered by name, so the same data always gives the same
    // list. Consultations without a diagnosis are left out.
    public static List<DiagnosisCount> Top(IEnumerable<string?> diagnoses, int limit) =>
        diagnoses
            .Where(diagnosis => diagnosis is not null)
            .GroupBy(diagnosis => diagnosis!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DiagnosisCount(group.Min(StringComparer.Ordinal)!, group.Count()))
            .OrderByDescending(diagnosis => diagnosis.Count)
            .ThenBy(diagnosis => diagnosis.Diagnosis, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
}
