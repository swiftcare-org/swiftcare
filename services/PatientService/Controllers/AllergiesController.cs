using PatientService.Models.Dtos;
using PatientService.Services;
using Microsoft.AspNetCore.Mvc;

namespace PatientService.Controllers;

[ApiController]
[Route("api/patients/{patientId:guid}/allergies")]
public sealed class AllergiesController : ControllerBase
{
    private const string ForbiddenMessage = "Forbidden";
    private const string AllergyNotFoundMessage = "Allergy not found";
    private const string PatientNotFoundMessage = "Patient not found";
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";

    private readonly IAllergyService _allergyService;
    private readonly ILogger<AllergiesController> _logger;

    public AllergiesController(IAllergyService allergyService, ILogger<AllergiesController> logger)
    {
        _allergyService = allergyService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AllergyResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllergies(Guid patientId, CancellationToken cancellationToken)
    {
        if (RejectIfRoleNotIn("Doctor", "Receptionist", "Admin") is { } forbidden)
        {
            return forbidden;
        }

        var allergies = await _allergyService.GetAllergiesAsync(patientId, cancellationToken);

        if (allergies is null)
        {
            return NotFound(new MessageResponse(PatientNotFoundMessage));
        }

        return Ok(allergies);
    }

    [HttpPost]
    [ProducesResponseType(typeof(AllergyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddAllergy(
        Guid patientId,
        [FromBody] AllergyRequest request,
        CancellationToken cancellationToken)
    {
        if (RejectIfRoleNotIn("Doctor", "Receptionist") is { } forbidden)
        {
            return forbidden;
        }

        var actingUserId = ParseUserIdHeader();
        var allergy = await _allergyService.AddAllergyAsync(patientId, request, actingUserId, cancellationToken);

        if (allergy is null)
        {
            return NotFound(new MessageResponse(PatientNotFoundMessage));
        }

        // No Location header: there is no GET for a single allergy to point at, matching
        // the same choice on POST /api/patients.
        return StatusCode(StatusCodes.Status201Created, allergy);
    }

    [HttpPut("{allergyId:guid}")]
    [ProducesResponseType(typeof(AllergyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAllergy(
        Guid patientId,
        Guid allergyId,
        [FromBody] AllergyRequest request,
        CancellationToken cancellationToken)
    {
        if (RejectIfRoleNotIn("Doctor", "Receptionist") is { } forbidden)
        {
            return forbidden;
        }

        var actingUserId = ParseUserIdHeader();
        var allergy = await _allergyService.UpdateAllergyAsync(
            patientId, allergyId, request, actingUserId, cancellationToken);

        if (allergy is null)
        {
            return NotFound(new MessageResponse(AllergyNotFoundMessage));
        }

        return Ok(allergy);
    }

    [HttpDelete("{allergyId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAllergy(
        Guid patientId,
        Guid allergyId,
        CancellationToken cancellationToken)
    {
        if (RejectIfRoleNotIn("Doctor", "Receptionist") is { } forbidden)
        {
            return forbidden;
        }

        var actingUserId = ParseUserIdHeader();
        var removed = await _allergyService.RemoveAllergyAsync(patientId, allergyId, actingUserId, cancellationToken);

        if (!removed)
        {
            return NotFound(new MessageResponse(AllergyNotFoundMessage));
        }

        return NoContent();
    }

    // X-User-Role is trusted only because GatewaySecretMiddleware already rejected any
    // request that didn't originate from the Gateway - see the identical comment on
    // PatientsController.RejectIfRoleNotIn for the full rationale. Duplicated rather than
    // shared: CLAUDE.md's no-premature-abstraction rule outweighs deduplicating a single
    // five-line header check across two controllers in the same service.
    private IActionResult? RejectIfRoleNotIn(params string[] allowedRoles)
    {
        var role = HttpContext.Request.Headers[UserRoleHeaderName].FirstOrDefault();
        if (role is not null && allowedRoles.Contains(role))
        {
            return null;
        }

        _logger.LogWarning("Rejected request from a disallowed role: userId={UserId}", ParseUserIdHeader());

        return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse(ForbiddenMessage));
    }

    private Guid ParseUserIdHeader()
    {
        var userIdHeader = HttpContext.Request.Headers[UserIdHeaderName].FirstOrDefault();
        return Guid.TryParse(userIdHeader, out var userId) ? userId : Guid.Empty;
    }
}
