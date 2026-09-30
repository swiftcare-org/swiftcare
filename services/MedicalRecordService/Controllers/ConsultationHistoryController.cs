using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedicalRecordService.Controllers;

[ApiController]
[Route("api/consultations/patient/{patientId:guid}")]
public sealed class ConsultationHistoryController : ControllerBase
{
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";

    private readonly IConsultationHistoryService _historyService;

    public ConsultationHistoryController(IConsultationHistoryService historyService)
    {
        _historyService = historyService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ConsultationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHistory(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        var denied = RejectUnlessDoctor();
        if (denied is not null)
        {
            return denied;
        }

        return Ok(await _historyService.GetHistoryAsync(patientId, cancellationToken));
    }

    [HttpGet("latest")]
    [ProducesResponseType(typeof(ConsultationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetLatest(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        var denied = RejectUnlessDoctor();
        if (denied is not null)
        {
            return denied;
        }

        var consultation = await _historyService.GetLatestAsync(patientId, cancellationToken);
        return consultation is null ? NoContent() : Ok(consultation);
    }

    // These identity headers are trusted only because GatewaySecretMiddleware has
    // already rejected requests that did not originate from the API Gateway.
    private IActionResult? RejectUnlessDoctor()
    {
        if (!string.Equals(
                HttpContext.Request.Headers[UserRoleHeaderName].FirstOrDefault(),
                "Doctor",
                StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse("Forbidden"));
        }

        var userIdHeader = HttpContext.Request.Headers[UserIdHeaderName].FirstOrDefault();
        if (!Guid.TryParse(userIdHeader, out var doctorId) || doctorId == Guid.Empty)
        {
            return Unauthorized(new MessageResponse("Doctor identity is unavailable"));
        }

        return null;
    }
}
