using AuthService.Models.Enums;

namespace AuthService.Models.Dtos;

public sealed class UserActionResult
{
    public required UserActionOutcome Outcome { get; init; }
    public UserSummaryResponse? User { get; init; }
}
