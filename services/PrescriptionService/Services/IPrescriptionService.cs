using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;

namespace PrescriptionService.Services;

public interface IPrescriptionService
{
    Task<CreatePrescriptionResult> CreateAsync(
        CreatePrescriptionRequest request,
        Guid doctorId,
        string doctorName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PrescriptionResponse>> GetForPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    Task<PrescriptionItemChangeResult> AddMedicineAsync(
        Guid prescriptionId,
        PrescriptionItemRequest medicine,
        Guid doctorId,
        CancellationToken cancellationToken = default);

    Task<PrescriptionItemChangeResult> RemoveMedicineAsync(
        Guid prescriptionId,
        Guid medicineId,
        Guid doctorId,
        CancellationToken cancellationToken = default);
}
