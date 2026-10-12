using PrescriptionService.Models.Dtos;
using PrescriptionService.Models.Entities;

namespace PrescriptionService.Services;

public static class NoPrescriptionResponses
{
    // Shaped like a prescription with the NOT_REQUIRED status and no medicines, so a
    // caller asking what happened for a queue entry reads one shape for every outcome.
    // CreatedAt carries the time the decision was recorded.
    public static PrescriptionResponse From(NoPrescriptionDecision decision) => new(
        decision.Id,
        decision.ConsultationId,
        decision.QueueId,
        decision.PatientId,
        decision.DoctorId,
        decision.DoctorName,
        NoPrescriptionDecision.NotRequiredStatus,
        DateTime.SpecifyKind(decision.RecordedAt, DateTimeKind.Utc),
        []);
}
