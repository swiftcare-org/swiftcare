namespace AuthService.Models.Dtos;

public sealed class LoginResponse
{
    public required string Token { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required UserResponse User { get; init; }
}

public sealed class UserResponse
{
    public required Guid UserId { get; init; }
    public required string FullName { get; init; }
    public required string Role { get; init; }

    // Populated for Doctor accounts only.
    public string? RoomNumber { get; init; }
}
