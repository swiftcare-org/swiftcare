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
    private readonly ITodayQueueService _todayQueueService;

    public QueueController(
        IPatientQueueStatusService patientQueueStatusService,
        ITodayQueueService todayQueueService)
    {
        _patientQueueStatusService = patientQueueStatusService;
        _todayQueueService = todayQueueService;
    }

    [HttpGet("today")]
    [ProducesResponseType(typeof(IReadOnlyList<TodayQueueEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetToday(CancellationToken cancellationToken)
    {
        if (!string.Equals(
                HttpContext.Request.Headers[UserRoleHeaderName].FirstOrDefault(),
                "Receptionist",
                StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse("Forbidden"));
        }

        var entries = await _todayQueueService.GetTodayAsync(cancellationToken);
        return Ok(entries);
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
