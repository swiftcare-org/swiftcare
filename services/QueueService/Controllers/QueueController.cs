using Microsoft.AspNetCore.Mvc;
using QueueService.Models.Dtos;
using QueueService.Services;

namespace QueueService.Controllers;

[ApiController]
[Route("api/queue")]
public sealed class QueueController : ControllerBase
{
    private const string UserRoleHeaderName = "X-User-Role";

    private readonly IPatientQueueStatusService _patientQueueStatusService;

    public QueueController(IPatientQueueStatusService patientQueueStatusService)
    {
        _patientQueueStatusService = patientQueueStatusService;
    }

    [HttpGet("today/patient/{patientId:guid}")]
    [ProducesResponseType(typeof(PatientQueueStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTodayPatientStatus(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                HttpContext.Request.Headers[UserRoleHeaderName].FirstOrDefault(),
                "Receptionist",
                StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse("Forbidden"));
        }

        var status = await _patientQueueStatusService.GetTodayStatusAsync(patientId, cancellationToken);
        return Ok(status);
    }
}
