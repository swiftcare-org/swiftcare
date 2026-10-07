using PrescriptionService.Models.Dtos;

namespace PrescriptionService.Models;

public enum RecordNoPrescriptionOutcome
{
    Recorded,
    ConsultationAlreadyHasPrescription,
    AlreadyRecorded
}

public sealed record RecordNoPrescriptionResult(
    RecordNoPrescriptionOutcome Outcome,
    PrescriptionResponse? Decision = null);
