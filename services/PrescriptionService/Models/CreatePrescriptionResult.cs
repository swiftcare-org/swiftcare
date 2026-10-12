using PrescriptionService.Models.Dtos;

namespace PrescriptionService.Models;

public enum CreatePrescriptionOutcome
{
    Success,
    ConsultationAlreadyHasPrescription,
    NoPrescriptionRequiredRecorded
}

public sealed record CreatePrescriptionResult(
    CreatePrescriptionOutcome Outcome,
    PrescriptionResponse? Prescription = null);
