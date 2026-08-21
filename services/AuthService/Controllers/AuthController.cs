using AuthService.Models.Dtos;
using AuthService.Models.Enums;
using AuthService.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private const string InvalidCredentialsMessage = "Invalid username or password";
    private const string AccountDeactivatedMessage = "Your account has been deactivated. Contact your administrator.";
    private const string UnauthorizedMessage = "Unauthorized";

    private readonly IAuthenticationService _authenticationService;

    public AuthController(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var result = await _authenticationService.LoginAsync(
            request.Username,
            request.Password,
            correlationId,
            ipAddress,
            cancellationToken);

        return result.Outcome switch
        {
            LoginOutcome.Success => Ok(new LoginResponse
            {
                Token = result.Token!,
                ExpiresAt = result.ExpiresAt!.Value,
                User = new UserResponse
                {
                    UserId = result.User!.UserId,
                    FullName = result.User.FullName,
                    Role = result.User.Role.ToString(),
                    RoomNumber = result.User.RoomNumber
                }
            }),
            LoginOutcome.AccountDeactivated => StatusCode(
                StatusCodes.Status403Forbidden,
                new MessageResponse(AccountDeactivatedMessage)),
            _ => Unauthorized(new MessageResponse(InvalidCredentialsMessage))
        };
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        // X-User-Id is trusted only because GatewaySecretMiddleware already rejected any
        // request that didn't originate from the Gateway, which is the sole source of this
        // header - it derives it from the validated JWT, never from the original client.
        var userIdHeader = HttpContext.Request.Headers["X-User-Id"].FirstOrDefault();
        if (!Guid.TryParse(userIdHeader, out var userId))
        {
            return Unauthorized(new MessageResponse(UnauthorizedMessage));
        }

        var correlationId = HttpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        await _authenticationService.LogoutAsync(userId, correlationId, ipAddress, cancellationToken);

        return NoContent();
    }
}
