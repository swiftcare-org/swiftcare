using MedicalRecordService.Models.Enums;

namespace MedicalRecordService.Models.Dtos;

public sealed record CompleteConsultationResult(CompleteConsultationOutcome Outcome, Guid? EventId = null);
