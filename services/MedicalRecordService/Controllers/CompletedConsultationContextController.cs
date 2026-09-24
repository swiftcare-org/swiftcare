using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedicalRecordService.Controllers;

[ApiController]
[Route("api/consultations/latest-completed")]
public sealed class CompletedConsultationContextController(
    IConsultationCompletionService completionService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(CompletedConsultationContextResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetLatest(CancellationToken cancellationToken)
    {
        if (!string.Equals(
                Request.Headers["X-User-Role"].FirstOrDefault(),
                "Doctor",
                StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse("Forbidden"));
        }

        var userIdHeader = Request.Headers["X-User-Id"].FirstOrDefault();
        if (!Guid.TryParse(userIdHeader, out var doctorId) || doctorId == Guid.Empty)
        {
            return Unauthorized(new MessageResponse("Doctor identity is unavailable"));
        }

        var consultation = await completionService.FindLatestCompletedAsync(
            doctorId,
            cancellationToken);

        return consultation is null ? NoContent() : Ok(consultation);
    }
}
