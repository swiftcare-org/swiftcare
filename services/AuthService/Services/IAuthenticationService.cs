using AuthService.Models.Dtos;

namespace AuthService.Services;

public interface IAuthenticationService
{
    Task<LoginResult> LoginAsync(
        string username,
        string password,
        string correlationId,
        string ipAddress,
        CancellationToken cancellationToken = default);
}
