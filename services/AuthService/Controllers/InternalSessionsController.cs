using AuthService.Models.Dtos;
using AuthService.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Controllers;

// Protected by GatewaySecretMiddleware and deliberately absent from public proxy routes.
[ApiController]
[Route("internal/sessions")]
public sealed class InternalSessionsController(SessionValidationService sessions) : ControllerBase
{
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateAsync(SessionValidationRequest request, CancellationToken cancellationToken) =>
        Ok(await sessions.ValidateAsync(request, cancellationToken));
}
