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
    private const string UserNotFoundMessage = "User not found";
    private const string RoomNumberRequiredMessage = "Room number is required for doctors";
    private static readonly string PasswordTooShortMessage =
        $"Password must be at least {PasswordPolicy.MinimumLength} characters";
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

        var result = await _userAccountService.CreateUserAsync(request, CreateActionContext(), cancellationToken);

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

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(UserSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (RejectIfNotAdmin() is { } forbidden)
        {
            return forbidden;
        }

        if (RejectIfEmptyUserId(id) is { } badRequest)
        {
            return badRequest;
        }

        var result = await _userAccountService.UpdateUserAsync(id, request, CreateActionContext(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("{id:guid}/reset-password")]
    [ProducesResponseType(typeof(UserSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(
        Guid id,
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (RejectIfNotAdmin() is { } forbidden)
        {
            return forbidden;
        }

        if (RejectIfEmptyUserId(id) is { } badRequest)
        {
            return badRequest;
        }

        var result = await _userAccountService.ResetPasswordAsync(
            id, request.NewPassword, CreateActionContext(), cancellationToken);
        return ToActionResult(result);
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

    // The route constraint accepts the all-zero GUID, which is not a real ID.
    private IActionResult? RejectIfEmptyUserId(Guid id) =>
        id == Guid.Empty ? BadRequest(new MessageResponse("User ID must be provided.")) : null;

    private IActionResult ToActionResult(UserActionResult result)
    {
        switch (result.Outcome)
        {
            case UserActionOutcome.Success:
                return Ok(result.User);
            case UserActionOutcome.RoomNumberRequiredForDoctor:
                ModelState.AddModelError(nameof(UpdateUserRequest.RoomNumber), RoomNumberRequiredMessage);
                return ValidationProblem(ModelState);
            case UserActionOutcome.PasswordTooShort:
                ModelState.AddModelError(nameof(ResetPasswordRequest.NewPassword), PasswordTooShortMessage);
                return ValidationProblem(ModelState);
            default:
                return NotFound(new MessageResponse(UserNotFoundMessage));
        }
    }

    private AdminActionContext CreateActionContext() => new(
        ParseUserIdHeader(),
        HttpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? Guid.NewGuid().ToString(),
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

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
                    PasswordTooShortMessage);
                break;
            case CreateUserOutcome.RoomNumberRequiredForDoctor:
                ModelState.AddModelError(nameof(CreateUserRequest.RoomNumber), RoomNumberRequiredMessage);
                break;
        }
    }
}
