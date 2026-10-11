using System.ComponentModel.DataAnnotations;

namespace AuthService.Models.Dtos;

public sealed record SessionValidationRequest(Guid UserId, Guid SessionVersion,
    [Required, StringLength(128)] string TokenId, long ExpiresAtUnixSeconds, bool Revoke);
