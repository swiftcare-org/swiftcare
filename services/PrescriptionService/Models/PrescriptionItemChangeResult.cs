using PrescriptionService.Models.Dtos;

namespace PrescriptionService.Models;

public enum PrescriptionItemChangeOutcome
{
    Success,
    PrescriptionNotFound,
    MedicineNotFound,
    MinimumOneMedicineRequired,
    PrescriptionDispensed,
    ConcurrentModification
}

public sealed record PrescriptionItemChangeResult(
    PrescriptionItemChangeOutcome Outcome,
    PrescriptionResponse? Prescription = null);
