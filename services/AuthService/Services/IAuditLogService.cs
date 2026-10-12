using AuthService.Models.Dtos;

namespace AuthService.Services;

public interface IAuditLogService
{
    Task<IReadOnlyList<AuditLogEntryResponse>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
