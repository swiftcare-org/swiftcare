using AuthService.Models.Enums;

namespace AuthService.Models.Dtos;

public sealed class LoginResult
{
    public required LoginOutcome Outcome { get; init; }
    public string? Token { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public AuthenticatedUser? User { get; init; }
}
