namespace AuthService.Models.Dtos;

public sealed class UserSummaryResponse
{
    public required Guid UserId { get; init; }
    public required string Username { get; init; }
    public required string FullName { get; init; }
    public required string Role { get; init; }

    // Populated for Doctor accounts only.
    public string? RoomNumber { get; init; }
    public required bool IsActive { get; init; }
    public required DateTime CreatedAt { get; init; }
}
