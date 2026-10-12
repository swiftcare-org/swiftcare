namespace MedicalRecordService.Models.Enums;

public enum CreateConsultationOutcome
{
    Success,
    FollowUpDateInPast,
    TemplateNotFound,
    QueueAlreadyHasConsultation
}
