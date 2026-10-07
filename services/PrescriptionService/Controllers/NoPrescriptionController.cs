using Microsoft.AspNetCore.Mvc;
using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Services;

namespace PrescriptionService.Controllers;

[ApiController]
[Route("api/consultations/{consultationId:guid}/no-prescription")]
public sealed class NoPrescriptionController(INoPrescriptionService noPrescriptionService)
    : ControllerBase
{
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";
    private const string UserNameHeaderName = "X-User-Name";

    [HttpPost]
    [ProducesResponseType(typeof(PrescriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordNoPrescription(
        Guid consultationId,
        [FromBody] RecordNoPrescriptionRequest request,
        CancellationToken cancellationToken)
    {
        // The identity headers are trusted because GatewaySecretMiddleware has already
        // rejected any request that did not come through the API Gateway.
        if (!string.Equals(
                Request.Headers[UserRoleHeaderName].FirstOrDefault(),
                "Doctor",
                StringComparison.Ordinal))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new MessageResponse("Forbidden"));
        }

        var userIdHeader = Request.Headers[UserIdHeaderName].FirstOrDefault();
        var doctorName = Request.Headers[UserNameHeaderName].FirstOrDefault();
        if (!Guid.TryParse(userIdHeader, out var doctorId)
            || doctorId == Guid.Empty
            || string.IsNullOrWhiteSpace(doctorName))
        {
            return Unauthorized(new MessageResponse("Doctor identity is unavailable"));
        }

        // The route constraint accepts the all-zero GUID, which is not a real ID.
        if (consultationId == Guid.Empty)
        {
            return BadRequest(new MessageResponse("Consultation ID must be provided."));
        }

        var result = await noPrescriptionService.RecordAsync(
            consultationId,
            request,
            doctorId,
            doctorName,
            cancellationToken);

        return result.Outcome switch
        {
            RecordNoPrescriptionOutcome.Recorded when result.Decision is not null =>
                StatusCode(StatusCodes.Status201Created, result.Decision),
            RecordNoPrescriptionOutcome.Recorded => throw new InvalidOperationException(
                "A recorded result must contain the decision."),
            RecordNoPrescriptionOutcome.ConsultationAlreadyHasPrescription => Conflict(
                new MessageResponse("A prescription already exists for this consultation")),
            RecordNoPrescriptionOutcome.AlreadyRecorded => Conflict(
                new MessageResponse("No prescription required is already recorded for this consultation")),
            _ => throw new InvalidOperationException(
                $"Unsupported record-no-prescription outcome: {result.Outcome}.")
        };
    }
}
