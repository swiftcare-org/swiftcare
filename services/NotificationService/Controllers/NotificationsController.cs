using Microsoft.AspNetCore.Mvc;
using NotificationService.Models.Dtos;
using NotificationService.Services;

namespace NotificationService.Controllers;

[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController(INotificationFeedService feedService) : ControllerBase
{
    private const string UserRoleHeaderName = "X-User-Role";

    // The feed is the department's front-desk view, so it is for reception and administrators.
    private static readonly string[] AllowedRoles = ["Receptionist", "Admin"];

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetNotifications(
        [FromHeader(Name = UserRoleHeaderName)] string? userRole,
        [FromQuery] int? limit,
        CancellationToken cancellationToken) =>
        await GetFeedAsync(userRole, limit, false, cancellationToken);

    [HttpGet("today")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTodayNotifications(
        [FromHeader(Name = UserRoleHeaderName)] string? userRole,
        [FromQuery] int? limit,
        CancellationToken cancellationToken) =>
        await GetFeedAsync(userRole, limit, true, cancellationToken);

    private async Task<IActionResult> GetFeedAsync(
        string? userRole, int? limit, bool today, CancellationToken cancellationToken)
    {
        // The role header is trusted because GatewaySecretMiddleware has already rejected
        // any request that did not come through the API Gateway.
        if (string.IsNullOrWhiteSpace(userRole))
        {
            return Unauthorized(new MessageResponse("User identity is unavailable"));
        }

        if (!AllowedRoles.Contains(userRole, StringComparer.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse("Forbidden"));
        }

        var requestedLimit = limit ?? NotificationFeedService.DefaultLimit;
        var notifications = today
            ? await feedService.GetTodayAsync(requestedLimit, cancellationToken)
            : await feedService.GetRecentAsync(requestedLimit, cancellationToken);

        return Ok(notifications);
    }
}
