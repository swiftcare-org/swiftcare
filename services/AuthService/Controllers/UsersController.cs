using AuthService.Models.Configuration;
using AuthService.Models.Dtos;
using AuthService.Models.Enums;
using AuthService.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private const string ForbiddenMessage = "Forbidden";
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";

    private readonly IUserAccountService _userAccountService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserAccountService userAccountService, ILogger<UsersController> logger)
    {
        _userAccountService = userAccountService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(UserSummaryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (RejectIfNotAdmin() is { } forbidden)
        {
            return forbidden;
        }

        var correlationId = HttpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString();
        var actingAdminId = ParseUserIdHeader();

        var result = await _userAccountService.CreateUserAsync(request, correlationId, actingAdminId, cancellationToken);

        if (result.Outcome != CreateUserOutcome.Success)
        {
            AddValidationError(result.Outcome);
            return ValidationProblem(ModelState);
        }

        // No Location header: there is no GET /api/users/{id} endpoint to point at, and
        // inventing an unrouted URL would be misleading.
        return StatusCode(StatusCodes.Status201Created, result.User);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        if (RejectIfNotAdmin() is { } forbidden)
        {
            return forbidden;
        }

        var users = await _userAccountService.GetUsersAsync(cancellationToken);
        return Ok(users);
    }

    // X-User-Role is trusted only because GatewaySecretMiddleware already rejected any
    // request that didn't originate from the Gateway, which is the sole source of this
    // header - it derives it from the validated JWT, never from the original client.
    // AuthService registers no authentication scheme, so [Authorize(Roles = "Admin")]
    // would compile and enforce nothing; this header check is the actual enforcement.
    private IActionResult? RejectIfNotAdmin()
    {
        var role = HttpContext.Request.Headers[UserRoleHeaderName].FirstOrDefault();
        if (role == nameof(UserRole.Admin))
        {
            return null;
        }

        // Logged as the parsed Guid, never the raw header, so an attacker who can reach
        // this endpoint directly (bypassing the Gateway) cannot inject newlines or other
        // control characters into the log stream via the X-User-Id header value.
        _logger.LogWarning("Rejected non-admin request to user management: userId={UserId}", ParseUserIdHeader());

        return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse(ForbiddenMessage));
    }

    private Guid ParseUserIdHeader()
    {
        var userIdHeader = HttpContext.Request.Headers[UserIdHeaderName].FirstOrDefault();
        return Guid.TryParse(userIdHeader, out var userId) ? userId : Guid.Empty;
    }

    private void AddValidationError(CreateUserOutcome outcome)
    {
        switch (outcome)
        {
            case CreateUserOutcome.DuplicateUsername:
                ModelState.AddModelError(nameof(CreateUserRequest.Username), "Username already exists");
                break;
            case CreateUserOutcome.PasswordTooShort:
                ModelState.AddModelError(
                    nameof(CreateUserRequest.Password),
                    $"Password must be at least {PasswordPolicy.MinimumLength} characters");
                break;
            case CreateUserOutcome.RoomNumberRequiredForDoctor:
                ModelState.AddModelError(nameof(CreateUserRequest.RoomNumber), "Room number is required for doctors");
                break;
        }
    }
}
