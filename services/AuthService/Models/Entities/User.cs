using AuthService.Models.Enums;

namespace AuthService.Models.Entities;

public sealed class User : IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string FullName { get; set; }
    public UserRole Role { get; set; }

    // Set only for Doctor accounts; omitted from the JWT for other roles.
    public string? RoomNumber { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
