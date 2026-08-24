using System.ComponentModel.DataAnnotations;
using AuthService.Models.Enums;

namespace AuthService.Models.Dtos;

public sealed class CreateUserRequest
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(64, ErrorMessage = "Username must be 64 characters or fewer.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(128, ErrorMessage = "Full name must be 128 characters or fewer.")]
    public string FullName { get; set; } = string.Empty;

    // Nullable so a missing role is a validation error rather than binding silently to
    // default(UserRole), which is Doctor.
    [Required(ErrorMessage = "Role is required.")]
    [EnumDataType(typeof(UserRole), ErrorMessage = "Role must be Doctor, Receptionist, or Admin.")]
    public UserRole? Role { get; set; }

    [StringLength(16, ErrorMessage = "Room number must be 16 characters or fewer.")]
    public string? RoomNumber { get; set; }
}
