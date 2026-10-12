using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Services;

namespace PrescriptionService.Controllers;

[ApiController]
[Route("api/prescriptions/report")]
public sealed class PrescriptionReportsController(IPrescriptionReportService reportService)
    : ControllerBase
{
    private const string UserRoleHeaderName = "X-User-Role";

    // The date is a clinic calendar day (yyyy-MM-dd). A value that is not a date is
    // rejected with 400 by model binding before this action runs.
    [HttpGet("daily")]
    [ProducesResponseType(typeof(PrescriptionDailyReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDailyReport(
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        // This identity header is trusted only because GatewaySecretMiddleware has already
        // rejected requests that did not originate from the API Gateway.
        if (!string.Equals(
                Request.Headers[UserRoleHeaderName].FirstOrDefault(),
                "Admin",
                StringComparison.Ordinal))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new MessageResponse("Forbidden"));
        }

        return Ok(await reportService.GetDailyReportAsync(date, cancellationToken));
    }

    // The month is a clinic calendar month (yyyy-MM). It is read as text and parsed here,
    // so a missing or impossible month gets one clear message.
    [HttpGet("monthly")]
    [ProducesResponseType(typeof(PrescriptionMonthlyReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMonthlyReport(
        [FromHeader(Name = UserRoleHeaderName)] string? userRole,
        [FromQuery] string? month,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(userRole, "Admin", StringComparison.Ordinal))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new MessageResponse("Forbidden"));
        }

        if (!DateOnly.TryParseExact(
                month,
                PrescriptionReportService.MonthFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var reportMonth))
        {
            return BadRequest(new MessageResponse("Month is required in the format yyyy-MM"));
        }

        return Ok(await reportService.GetMonthlyReportAsync(reportMonth, cancellationToken));
    }
}
