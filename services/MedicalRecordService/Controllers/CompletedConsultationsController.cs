using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedicalRecordService.Controllers;

[ApiController]
[Route("api/consultations/completed")]
public sealed class CompletedConsultationsController(IConsultationCompletionService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int page = 0, CancellationToken cancellationToken = default)
    {
        if (Request.Headers["X-User-Role"].FirstOrDefault() != "Doctor")
            return StatusCode(403, new MessageResponse("Forbidden"));
        if (!Guid.TryParse(Request.Headers["X-User-Id"].FirstOrDefault(), out var doctor) || doctor == Guid.Empty)
            return Unauthorized(new MessageResponse("Doctor identity is unavailable"));
        if (page < 0 || page > int.MaxValue / 50)
            return BadRequest(new MessageResponse("Page is outside the supported range"));
        return Ok(await service.FindCompletedPageAsync(doctor, page, cancellationToken));
    }
}
