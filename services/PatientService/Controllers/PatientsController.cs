using PatientService.Models.Dtos;
using PatientService.Models.Enums;
using PatientService.Services;
using Microsoft.AspNetCore.Mvc;

namespace PatientService.Controllers;

[ApiController]
[Route("api/patients")]
public sealed class PatientsController : ControllerBase
{
    private const string ForbiddenMessage = "Forbidden";
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";
    private const string CorrelationIdHeaderName = "X-Correlation-ID";

    private readonly IPatientRegistrationService _patientRegistrationService;
    private readonly ILogger<PatientsController> _logger;

    public PatientsController(
        IPatientRegistrationService patientRegistrationService,
        ILogger<PatientsController> logger)
    {
        _patientRegistrationService = patientRegistrationService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(RegisteredPatientResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RegisterPatient(
        [FromBody] RegisterPatientRequest request,
        CancellationToken cancellationToken)
    {
        if (RejectIfNotReceptionist() is { } forbidden)
        {
            return forbidden;
        }

        var correlationId = HttpContext.Request.Headers[CorrelationIdHeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString();
        var actingUserId = ParseUserIdHeader();

        var result = await _patientRegistrationService.RegisterPatientAsync(
            request, correlationId, actingUserId, cancellationToken);

        if (result.Outcome != RegisterPatientOutcome.Success)
        {
            AddValidationError(result.Outcome);
            return ValidationProblem(ModelState);
        }

        // No Location header: there is no GET /api/patients/{id} endpoint to point at, and
        // inventing an unrouted URL would be misleading.
        return StatusCode(StatusCodes.Status201Created, result.Patient);
    }

    // X-User-Role is trusted only because GatewaySecretMiddleware already rejected any
    // request that didn't originate from the Gateway, which is the sole source of this
    // header - it derives it from the validated JWT, never from the original client.
    // PatientService registers no authentication scheme, so [Authorize(Roles = ...)]
    // would compile and enforce nothing; this header check is the actual enforcement.
    private IActionResult? RejectIfNotReceptionist()
    {
        var role = HttpContext.Request.Headers[UserRoleHeaderName].FirstOrDefault();
        if (role is "Receptionist")
        {
            return null;
        }

        // Logged as the parsed Guid, never the raw header, so an attacker who can reach
        // this endpoint directly (bypassing the Gateway) cannot inject newlines or other
        // control characters into the log stream via the X-User-Id header value.
        _logger.LogWarning("Rejected non-receptionist request to patient registration: userId={UserId}", ParseUserIdHeader());

        return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse(ForbiddenMessage));
    }

    private Guid ParseUserIdHeader()
    {
        var userIdHeader = HttpContext.Request.Headers[UserIdHeaderName].FirstOrDefault();
        return Guid.TryParse(userIdHeader, out var userId) ? userId : Guid.Empty;
    }

    private void AddValidationError(RegisterPatientOutcome outcome)
    {
        if (outcome == RegisterPatientOutcome.DuplicateNic)
        {
            ModelState.AddModelError(
                nameof(RegisterPatientRequest.Nic),
                "A patient with this NIC already exists. Please search for the existing patient.");
        }
    }
}
