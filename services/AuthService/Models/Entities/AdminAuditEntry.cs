using AuthService.Models.Enums;

namespace AuthService.Models.Entities;

public sealed class AdminAuditEntry : IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // The admin who performed the action, taken from the Gateway-trusted X-User-Id.
    public Guid ActorUserId { get; set; }

    public AdminAuditAction Action { get; set; }

    // The account the action was performed on.
    public Guid TargetUserId { get; set; }

    public required string CorrelationId { get; set; }
    public required string IpAddress { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
