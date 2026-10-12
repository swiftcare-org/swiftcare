namespace AuthService.Models.Entities;

public sealed class RevokedSession
{
    public required string TokenId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
