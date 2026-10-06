using System.ComponentModel.DataAnnotations;

namespace AuthService.Models.Dtos;

// There is deliberately no Username or Role here: neither can be changed after creation.
public sealed class UpdateUserRequest
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(128, ErrorMessage = "Full name must be 128 characters or fewer.")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(16, ErrorMessage = "Room number must be 16 characters or fewer.")]
    public string? RoomNumber { get; set; }

    [StringLength(64, ErrorMessage = "Specialization must be 64 characters or fewer.")]
    public string? Specialization { get; set; }
}
