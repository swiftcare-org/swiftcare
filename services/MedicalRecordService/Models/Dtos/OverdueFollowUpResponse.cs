namespace MedicalRecordService.Models.Dtos;

// DaysOverdue counts whole clinic days since the follow-up date, so a follow-up due
// yesterday is 1 day overdue. DoctorName is the doctor who recorded the follow-up.
public sealed record OverdueFollowUpResponse(
    Guid ConsultationId,
    DateOnly FollowUpDate,
    string Instructions,
    string DoctorName,
    int DaysOverdue);
