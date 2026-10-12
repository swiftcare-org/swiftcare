namespace NotificationService.Models.Dtos;

// The department's activity on one clinic day. TotalPatients counts each patient who
// checked in once, so TotalPatients == NewPatients + ReturningPatients.
public sealed record DailyReportResponse(
    DateOnly Date,
    int TotalPatients,
    int NewPatients,
    int ReturningPatients,
    IReadOnlyList<RoomPatientCount> PatientsPerRoom,
    IReadOnlyList<DiagnosisCount> TopDiagnoses);

public sealed record RoomPatientCount(string RoomNumber, int Patients);

public sealed record DiagnosisCount(string Diagnosis, int Count);
