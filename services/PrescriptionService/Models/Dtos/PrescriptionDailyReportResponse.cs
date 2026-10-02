namespace PrescriptionService.Models.Dtos;

// TotalWritten counts prescriptions created on the clinic day; the other two split that
// same set by its current status, so TotalWritten == TotalDispensed + TotalPending.
public sealed record PrescriptionDailyReportResponse(
    DateOnly Date,
    int TotalWritten,
    int TotalDispensed,
    int TotalPending);
