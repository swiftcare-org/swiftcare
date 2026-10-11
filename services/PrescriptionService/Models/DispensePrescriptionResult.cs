using PrescriptionService.Models.Dtos;

namespace PrescriptionService.Models;

public enum DispensePrescriptionOutcome
{
    Success,
    PrescriptionNotFound,
    AlreadyDispensed,
    ConcurrentModification
}

public sealed record DispensePrescriptionResult(
    DispensePrescriptionOutcome Outcome,
    PrescriptionResponse? Prescription = null);
