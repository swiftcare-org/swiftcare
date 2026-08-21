using AuthService.Models.Enums;

namespace AuthService.Models.Dtos;

public sealed class AuthenticatedUser
{
    public required Guid UserId { get; init; }
    public required string FullName { get; init; }
    public required UserRole Role { get; init; }

    // Populated for Doctor accounts only.
    public string? RoomNumber { get; init; }
}
