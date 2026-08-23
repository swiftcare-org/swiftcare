using AuthService.Models.Enums;

namespace AuthService.Models.Entities;

public sealed class LoginAuditEntry : IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Null when the submitted username did not resolve to a user. The
    // attempted username itself is never stored here, since users
    // routinely mistype a password into the username field.
    public Guid? UserId { get; set; }

    public LoginOutcome Outcome { get; set; }
    public required string CorrelationId { get; set; }
    public required string IpAddress { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
