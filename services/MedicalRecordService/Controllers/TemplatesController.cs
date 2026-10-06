using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Enums;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedicalRecordService.Controllers;

[ApiController]
[Route("api/templates")]
public sealed class TemplatesController : ControllerBase
{
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";

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
        if (RejectUnlessDoctor(out var doctorId) is { } rejection)
        {
            return rejection;
        }

        var templates = await _templateService.GetTemplatesForDoctorAsync(doctorId, cancellationToken);
        return Ok(templates);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ConsultationTemplateResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTemplate(
        [FromBody] CreateConsultationTemplateRequest request,
        CancellationToken cancellationToken)
    {
        if (RejectUnlessDoctor(out var doctorId) is { } rejection)
        {
            return rejection;
        }

        var result = await _templateService.CreateAsync(request, doctorId, cancellationToken);

        return result.Outcome switch
        {
            CreateTemplateOutcome.Created when result.Template is not null =>
                StatusCode(StatusCodes.Status201Created, result.Template),
            CreateTemplateOutcome.Created => throw new InvalidOperationException(
                "A successful template result must include the created template."),
            _ => Conflict(new MessageResponse("A template with this name already exists"))
        };
    }

    // The identity headers are trusted only after GatewaySecretMiddleware proves that the
    // API Gateway forwarded the request and rebuilt identity from a validated JWT. The
    // owner of a template always comes from here, never from the request body.
    private IActionResult? RejectUnlessDoctor(out Guid doctorId)
    {
        doctorId = Guid.Empty;

        if (!string.Equals(
                HttpContext.Request.Headers[UserRoleHeaderName].FirstOrDefault(),
                "Doctor",
                StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse("Forbidden"));
        }

        if (!Guid.TryParse(HttpContext.Request.Headers[UserIdHeaderName].FirstOrDefault(), out doctorId)
            || doctorId == Guid.Empty)
        {
            return Unauthorized(new MessageResponse("Doctor identity is unavailable"));
        }

        return null;
    }
}
