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
    private readonly IPatientSearchService _patientSearchService;
    private readonly IPatientProfileService _patientProfileService;
    private readonly ILogger<PatientsController> _logger;

    public PatientsController(
        IPatientRegistrationService patientRegistrationService,
        IPatientSearchService patientSearchService,
        IPatientProfileService patientProfileService,
        ILogger<PatientsController> logger)
    {
        _patientRegistrationService = patientRegistrationService;
        _patientSearchService = patientSearchService;
        _patientProfileService = patientProfileService;
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
        if (RejectIfRoleNotIn("Receptionist") is { } forbidden)
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

    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyList<PatientSearchResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SearchPatients(
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        // Widened from Receptionist-only (SWC-12) to also admit Doctor and Admin: SWC-17
        // gives all three roles access to a patient's profile and allergies, and search is
        // the only navigation path to a profile - without this a doctor could never reach
        // one.
        if (RejectIfRoleNotIn("Doctor", "Receptionist", "Admin") is { } forbidden)
        {
            return forbidden;
        }

        var results = await _patientSearchService.SearchPatientsAsync(q, cancellationToken);

        // The search term is PHI - it is a patient's name, NIC, or phone number - so it is
        // never logged, at any level. Only the acting user and the result count are.
        _logger.LogInformation(
            "Patient search executed: resultCount={ResultCount} by userId={UserId}",
            results.Count,
            ParseUserIdHeader());

        return Ok(results);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PatientProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPatient(Guid id, CancellationToken cancellationToken)
    {
        if (RejectIfRoleNotIn("Doctor", "Receptionist", "Admin") is { } forbidden)
        {
            return forbidden;
        }

        var profile = await _patientProfileService.GetPatientAsync(id, cancellationToken);

        if (profile is null)
        {
            return NotFound(new MessageResponse("Patient not found"));
        }

        return Ok(profile);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PatientProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePatient(
        Guid id,
        [FromBody] UpdatePatientRequest request,
        CancellationToken cancellationToken)
    {
        if (RejectIfRoleNotIn("Receptionist") is { } forbidden)
        {
            return forbidden;
        }

        var profile = await _patientProfileService.UpdatePatientAsync(id, request, cancellationToken);

        if (profile is null)
        {
            return NotFound(new MessageResponse("Patient not found"));
        }

        _logger.LogInformation(
            "Patient profile updated: patientId={PatientId} by userId={UserId}",
            id,
            ParseUserIdHeader());

        return Ok(profile);
    }

    // X-User-Role is trusted only because GatewaySecretMiddleware already rejected any
    // request that didn't originate from the Gateway, which is the sole source of this
    // header - it derives it from the validated JWT, never from the original client.
    // PatientService registers no authentication scheme, so [Authorize(Roles = ...)]
    // would compile and enforce nothing; this header check is the actual enforcement.
    private IActionResult? RejectIfRoleNotIn(params string[] allowedRoles)
    {
        var role = HttpContext.Request.Headers[UserRoleHeaderName].FirstOrDefault();
        if (role is not null && allowedRoles.Contains(role))
        {
            return null;
        }

        // Logged as the parsed Guid, never the raw header, so an attacker who can reach
        // this endpoint directly (bypassing the Gateway) cannot inject newlines or other
        // control characters into the log stream via the X-User-Id header value.
        _logger.LogWarning("Rejected request from a disallowed role: userId={UserId}", ParseUserIdHeader());

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
