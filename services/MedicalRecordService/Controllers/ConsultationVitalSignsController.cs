using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Enums;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedicalRecordService.Controllers;

[ApiController]
[Route("api/consultations/{consultationId:guid}/vitals")]
public sealed class ConsultationVitalSignsController : ControllerBase
{
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";

    private readonly IVitalSignsService _vitalSignsService;

    public ConsultationVitalSignsController(IVitalSignsService vitalSignsService)
    {
        _vitalSignsService = vitalSignsService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(VitalSignsResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordVitalSigns(
        Guid consultationId,
        [FromBody] RecordVitalSignsRequest request,
        CancellationToken cancellationToken)
    {
        // These identity headers are trusted only because GatewaySecretMiddleware has
        // already rejected requests that did not originate from the API Gateway.
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

        var result = await _vitalSignsService.RecordAsync(
            consultationId,
            doctorId,
            request,
            cancellationToken);

        return result.Outcome switch
        {
            RecordVitalSignsOutcome.Success when result.VitalSigns is not null =>
                StatusCode(StatusCodes.Status201Created, result.VitalSigns),
            RecordVitalSignsOutcome.Success => throw new InvalidOperationException(
                "A successful vital-signs result must include the recorded values."),
            RecordVitalSignsOutcome.ConsultationNotFound => NotFound(
                new MessageResponse("Consultation was not found for this doctor")),
            RecordVitalSignsOutcome.VitalSignsAlreadyExist => Conflict(
                new MessageResponse("Vital signs already exist for this consultation")),
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                result.Outcome,
                "Unsupported record-vital-signs outcome.")
        };
    }
}
