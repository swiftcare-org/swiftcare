using MedicalRecordService.Models.Events;

namespace MedicalRecordService.Models.Dtos;

public enum CompletionPreparationOutcome
{
    Ready,
    ConsultationNotFound,
    VitalSignsMissing
}

public sealed record CompletionPreparationResult(
    CompletionPreparationOutcome Outcome,
    ConsultationCompletedEvent? Event = null);
