namespace AuthService.Models.Dtos;

public sealed class AuditLogEntryResponse
{
    public required Guid Id { get; init; }

    // Who acted. "Unknown" for a sign-in attempt that matched no account.
    public required string Username { get; init; }

    public required string Action { get; init; }

    // The account an admin action was performed on; absent for sign-in and sign-out.
    public string? TargetUsername { get; init; }

    public required DateTime OccurredAt { get; init; }
    public required string IpAddress { get; init; }
}
