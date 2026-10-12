namespace PrescriptionService.Models.Dtos;

// Month is "yyyy-MM". TotalWritten counts prescriptions created in that month of clinic
// days; TotalDispensed counts how many of those have been dispensed so far.
public sealed record PrescriptionMonthlyReportResponse(
    string Month,
    int TotalWritten,
    int TotalDispensed);
