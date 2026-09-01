using AuthService.Models.Enums;

namespace AuthService.Models.Dtos;

public sealed class CreateUserResult
{
    public required CreateUserOutcome Outcome { get; init; }
    public UserSummaryResponse? User { get; init; }
}
