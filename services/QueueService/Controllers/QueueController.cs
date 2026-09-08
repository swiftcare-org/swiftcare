using Microsoft.AspNetCore.Mvc;
using QueueService.Models.Dtos;
using QueueService.Models.Enums;
using QueueService.Services;

namespace QueueService.Controllers;

[ApiController]
[Route("api/queue")]
public sealed class QueueController : ControllerBase
{
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";
    private const string UserNameHeaderName = "X-User-Name";
    private const string RoomNumberHeaderName = "X-Room-Number";
    private const string CorrelationIdHeaderName = "X-Correlation-ID";

    private readonly IPatientQueueStatusService _patientQueueStatusService;
    private readonly ITodayQueueService _todayQueueService;
    private readonly ICallNextPatientService _callNextPatientService;

    public QueueController(
        IPatientQueueStatusService patientQueueStatusService,
        ITodayQueueService todayQueueService,
        ICallNextPatientService callNextPatientService)
    {
        _patientQueueStatusService = patientQueueStatusService;
        _todayQueueService = todayQueueService;
        _callNextPatientService = callNextPatientService;
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

    [HttpGet("today/waiting")]
    [ProducesResponseType(typeof(IReadOnlyList<TodayQueueEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetWaiting(CancellationToken cancellationToken)
    {
        if (!string.Equals(
                HttpContext.Request.Headers[UserRoleHeaderName].FirstOrDefault(),
                "Doctor",
                StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse("Forbidden"));
        }

        var entries = await _todayQueueService.GetWaitingAsync(cancellationToken);
        return Ok(entries);
    }

    [HttpPut("call-next")]
    [ProducesResponseType(typeof(CalledPatientResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CallNext(CancellationToken cancellationToken)
    {
        if (!string.Equals(
                HttpContext.Request.Headers[UserRoleHeaderName].FirstOrDefault(),
                "Doctor",
                StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse("Forbidden"));
        }

        var userIdHeader = HttpContext.Request.Headers[UserIdHeaderName].FirstOrDefault();
        var doctorName = HttpContext.Request.Headers[UserNameHeaderName].FirstOrDefault();
        var roomNumber = HttpContext.Request.Headers[RoomNumberHeaderName].FirstOrDefault();

        if (!Guid.TryParse(userIdHeader, out var doctorId)
            || doctorId == Guid.Empty
            || string.IsNullOrWhiteSpace(doctorName)
            || string.IsNullOrWhiteSpace(roomNumber))
        {
            return Unauthorized(new MessageResponse("Doctor identity is unavailable"));
        }

        var correlationIdHeader = HttpContext.Request.Headers[CorrelationIdHeaderName].FirstOrDefault();
        var correlationId = string.IsNullOrWhiteSpace(correlationIdHeader)
            ? Guid.NewGuid().ToString()
            : correlationIdHeader;

        var result = await _callNextPatientService.CallNextAsync(
            doctorId,
            doctorName,
            roomNumber,
            correlationId,
            cancellationToken);

        return result.Outcome switch
        {
            CallNextPatientOutcome.Success when result.CalledPatient is not null => Ok(result.CalledPatient),
            CallNextPatientOutcome.Success => throw new InvalidOperationException(
                "A successful call-next result must include the called patient."),
            CallNextPatientOutcome.NoPatientsWaiting => NotFound(
                new MessageResponse("No patients currently waiting")),
            CallNextPatientOutcome.DoctorOrRoomOccupied => Conflict(
                new MessageResponse("Complete current consultation first")),
            CallNextPatientOutcome.EventPublishFailed => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new MessageResponse("Unable to call next patient. Please try again.")),
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                result.Outcome,
                "Unsupported call-next outcome.")
        };
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
