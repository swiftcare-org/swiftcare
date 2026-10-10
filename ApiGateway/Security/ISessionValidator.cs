using System.Security.Claims;

namespace ApiGateway.Security;

public interface ISessionValidator
{
    Task<bool> ValidateAsync(ClaimsPrincipal user, bool revoke, CancellationToken cancellationToken);
}
