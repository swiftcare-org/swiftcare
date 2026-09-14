using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Enums;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedicalRecordService.Controllers;

[ApiController]
[Route("api/consultations")]
public sealed class ConsultationsController : ControllerBase
{
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";
    private const string UserNameHeaderName = "X-User-Name";
    private const string RoomNumberHeaderName = "X-Room-Number";

    private readonly IConsultationService _consultationService;

    public ConsultationsController(IConsultationService consultationService)
    {
        _consultationService = consultationService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ConsultationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateConsultation(
        [FromBody] CreateConsultationRequest request,
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
        var doctorName = HttpContext.Request.Headers[UserNameHeaderName].FirstOrDefault();
        var roomNumber = HttpContext.Request.Headers[RoomNumberHeaderName].FirstOrDefault();

        if (!Guid.TryParse(userIdHeader, out var doctorId)
            || doctorId == Guid.Empty
            || string.IsNullOrWhiteSpace(doctorName)
            || string.IsNullOrWhiteSpace(roomNumber))
        {
            return Unauthorized(new MessageResponse("Doctor identity is unavailable"));
        }

        var result = await _consultationService.CreateAsync(
            request,
            doctorId,
            doctorName,
            roomNumber,
            cancellationToken);

        return result.Outcome switch
        {
            CreateConsultationOutcome.Success when result.Consultation is not null =>
                StatusCode(StatusCodes.Status201Created, result.Consultation),
            CreateConsultationOutcome.Success => throw new InvalidOperationException(
                "A successful consultation result must include the created consultation."),
            CreateConsultationOutcome.TemplateNotFound => InvalidTemplateResponse(),
            CreateConsultationOutcome.QueueAlreadyHasConsultation => Conflict(
                new MessageResponse("A consultation already exists for this queue entry")),
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                result.Outcome,
                "Unsupported create-consultation outcome.")
        };
    }

    private IActionResult InvalidTemplateResponse()
    {
        ModelState.AddModelError(
            nameof(CreateConsultationRequest.TemplateId),
            "Selected template is unavailable");
        return ValidationProblem(ModelState);
    }
}
