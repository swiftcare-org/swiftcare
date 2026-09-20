using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedicalRecordService.Controllers;

[ApiController]
[Route("api/consultations/by-queue/{queueId:guid}")]
public sealed class ConsultationProgressController : ControllerBase
{
    private readonly IConsultationCompletionService _completionService;

    public ConsultationProgressController(IConsultationCompletionService completionService)
    {
        _completionService = completionService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ConsultationProgressResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetForQueue(Guid queueId, CancellationToken cancellationToken)
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

        var progress = await _completionService.FindByQueueAsync(queueId, doctorId, cancellationToken);
        return progress is null ? NoContent() : Ok(progress);
    }
}
