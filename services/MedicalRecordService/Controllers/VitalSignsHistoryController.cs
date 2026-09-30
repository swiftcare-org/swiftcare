using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedicalRecordService.Controllers;

[ApiController]
[Route("api/vitals/patient/{patientId:guid}")]
public sealed class VitalSignsHistoryController : ControllerBase
{
    private readonly IVitalSignsHistoryService _historyService;

    public VitalSignsHistoryController(IVitalSignsHistoryService historyService)
    {
        _historyService = historyService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<VitalSignsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHistory(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        // These identity headers are trusted only because GatewaySecretMiddleware has
        // already rejected requests that did not originate from the API Gateway.
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

        return Ok(await _historyService.GetHistoryAsync(patientId, cancellationToken));
    }
}
