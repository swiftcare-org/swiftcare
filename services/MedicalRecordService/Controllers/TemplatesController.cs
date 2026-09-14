using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedicalRecordService.Controllers;

[ApiController]
[Route("api/templates")]
public sealed class TemplatesController : ControllerBase
{
    private const string UserRoleHeaderName = "X-User-Role";

    private readonly IConsultationTemplateService _templateService;

    public TemplatesController(IConsultationTemplateService templateService)
    {
        _templateService = templateService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ConsultationTemplateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTemplates(CancellationToken cancellationToken)
    {
        // The role header is trusted only after GatewaySecretMiddleware proves that the
        // API Gateway forwarded the request and rebuilt identity from a validated JWT.
        if (!string.Equals(
                HttpContext.Request.Headers[UserRoleHeaderName].FirstOrDefault(),
                "Doctor",
                StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse("Forbidden"));
        }

        var templates = await _templateService.GetActiveTemplatesAsync(cancellationToken);
        return Ok(templates);
    }
}
