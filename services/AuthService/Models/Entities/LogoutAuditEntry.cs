namespace AuthService.Models.Entities;

public sealed class LogoutAuditEntry : IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Always known: this endpoint is only reachable with a Gateway-trusted X-User-Id,
    // unlike login where the username may not resolve to an account.
    public Guid UserId { get; set; }

    public required string CorrelationId { get; set; }
    public required string IpAddress { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
