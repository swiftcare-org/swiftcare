using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedicalRecordService.Controllers;

[ApiController]
[Route("api/consultations/patient/{patientId:guid}/latest-follow-up")]
public sealed class ConsultationFollowUpController : ControllerBase
{
    private readonly IConsultationFollowUpService _followUpService;

    public ConsultationFollowUpController(IConsultationFollowUpService followUpService)
    {
        _followUpService = followUpService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(OverdueFollowUpResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetLatestOverdue(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                HttpContext.Request.Headers["X-User-Role"].FirstOrDefault(),
                "Doctor",
                StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse("Forbidden"));
        }

        var userIdHeader = HttpContext.Request.Headers["X-User-Id"].FirstOrDefault();
        if (!Guid.TryParse(userIdHeader, out var doctorId) || doctorId == Guid.Empty)
        {
            return Unauthorized(new MessageResponse("Doctor identity is unavailable"));
        }

        var followUp = await _followUpService.FindOverdueAsync(patientId, cancellationToken);
        return followUp is null ? NoContent() : Ok(followUp);
    }
}
