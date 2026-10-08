using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Models.Dtos;
using NotificationService.Services;

namespace NotificationService.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController(IDailyReportService dailyReportService) : ControllerBase
{
    public const string DateFormat = "yyyy-MM-dd";

    private const string UserRoleHeaderName = "X-User-Role";
    private const string AdminRole = "Admin";

    [HttpGet("daily")]
    [ProducesResponseType(typeof(DailyReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDailyReport(
        [FromHeader(Name = UserRoleHeaderName)] string? userRole,
        [FromQuery] string? date,
        CancellationToken cancellationToken)
    {
        // The role header is trusted because GatewaySecretMiddleware has already rejected
        // any request that did not come through the API Gateway.
        if (string.IsNullOrWhiteSpace(userRole))
        {
            return Unauthorized(new MessageResponse("User identity is unavailable"));
        }

        if (!string.Equals(userRole, AdminRole, StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse("Forbidden"));
        }

        // Read as text and parsed here, so a missing or impossible date gets one clear message.
        if (!DateOnly.TryParseExact(date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var reportDate))
        {
            return BadRequest(new MessageResponse($"Date is required in the format {DateFormat}"));
        }

        return Ok(await dailyReportService.GetAsync(reportDate, cancellationToken));
    }
}
