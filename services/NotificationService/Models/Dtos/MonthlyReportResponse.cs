namespace NotificationService.Models.Dtos;

// The department activity in one calendar month of clinic days. TotalPatients counts
// each patient who checked in that month once, so TotalPatients == NewPatients +
// ReturningPatients, and the four weekly counts add up to it as well.
public sealed record MonthlyReportResponse(
    string Month,
    int TotalPatients,
    int NewPatients,
    int ReturningPatients,
    IReadOnlyList<DiagnosisCount> TopDiagnoses,
    IReadOnlyList<WeekPatientCount> WeeklyBreakdown);

// Week 1 is days 1 to 7, Week 2 days 8 to 14, Week 3 days 15 to 21 and Week 4 day 22 to
// the end of the month. A patient is counted in the week of their first visit that month.
public sealed record WeekPatientCount(int Week, int Patients);
