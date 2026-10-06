using System.ComponentModel.DataAnnotations;

namespace AuthService.Models.Dtos;

public sealed class ResetPasswordRequest
{
    [Required(ErrorMessage = "Password is required.")]
    public string NewPassword { get; set; } = string.Empty;
}
