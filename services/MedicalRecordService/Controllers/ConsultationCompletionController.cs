using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Enums;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedicalRecordService.Controllers;

[ApiController]
[Route("api/consultations/{consultationId:guid}/complete")]
public sealed class ConsultationCompletionController : ControllerBase
{
    private readonly IConsultationCompletionService _completionService;

    public ConsultationCompletionController(IConsultationCompletionService completionService)
    {
        _completionService = completionService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CompleteConsultationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Complete(
        Guid consultationId,
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

        var result = await _completionService.CompleteAsync(
            consultationId, doctorId, cancellationToken);

        return result.Outcome switch
        {
            CompleteConsultationOutcome.Success when result.EventId is Guid eventId =>
                Ok(new CompleteConsultationResponse(eventId)),
            CompleteConsultationOutcome.ConsultationNotFound =>
                NotFound(new MessageResponse("Consultation was not found for this doctor")),
            CompleteConsultationOutcome.VitalSignsMissing =>
                Conflict(new MessageResponse("Please save vital signs first")),
            CompleteConsultationOutcome.PublishFailed =>
                StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new MessageResponse("Consultation could not be completed. Please try again.")),
            _ => throw new InvalidOperationException("Unsupported consultation completion result.")
        };
    }
}
