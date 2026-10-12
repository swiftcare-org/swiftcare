using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Services;

public interface IConsultationService
{
    Task<CreateConsultationResult> CreateAsync(
        CreateConsultationRequest request,
        Guid doctorId,
        string doctorName,
        string roomNumber,
        CancellationToken cancellationToken = default);
}
