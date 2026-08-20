using AuthService.Models.Entities;

namespace AuthService.Services;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) GenerateToken(User user);
}
