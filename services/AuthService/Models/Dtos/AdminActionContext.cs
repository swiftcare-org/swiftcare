namespace AuthService.Models.Dtos;

// Who performed an account management action and from where, as recorded in the audit log.
public sealed record AdminActionContext(Guid AdminUserId, string CorrelationId, string IpAddress);
