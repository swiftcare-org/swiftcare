using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Data;

public interface IVisitContextRepository
{
    Task<VisitContextResponse?> FindAsync(Guid consultationId, CancellationToken cancellationToken);
}
