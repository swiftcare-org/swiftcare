using AuthService.Models.Dtos;
using AuthService.Models.Enums;
using AuthService.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Controllers;

// Read-only by design: there is no endpoint that edits or removes an audit entry.
[ApiController]
[Route("api/audit-logs")]
public sealed class AuditLogsController : ControllerBase
{
    private readonly IAuditLogService _auditLogService;

    public AuditLogsController(IAuditLogService auditLogService)
    {
        _auditLogService = auditLogService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AuditLogEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAuditLog([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        // X-User-Role is set only by the Gateway from the validated JWT; see UsersController.
        var role = HttpContext.Request.Headers["X-User-Role"].FirstOrDefault();
        if (role != nameof(UserRole.Admin))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse("Forbidden"));
        }

        var entries = await _auditLogService.GetRecentAsync(
            limit ?? AuditLogService.DefaultLimit, cancellationToken);
        return Ok(entries);
    }
}
