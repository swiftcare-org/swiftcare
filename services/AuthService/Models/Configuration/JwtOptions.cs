namespace AuthService.Models.Configuration;

public sealed class JwtOptions
{
    public required string SecretKey { get; set; }
    public required string Issuer { get; set; }
    public required string Audience { get; set; }
    public int ExpiryHours { get; set; } = 12;
}
