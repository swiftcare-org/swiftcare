using Microsoft.AspNetCore.Mvc;
using PatientService.Models.Dtos;
using PatientService.Services;

namespace PatientService.Controllers;

[ApiController]
[Route("api/patients/{patientId:guid}/conditions")]
public sealed class ChronicConditionsController : ControllerBase
{
    private const string ForbiddenMessage = "Forbidden";
    private const string ConditionNotFoundMessage = "Chronic condition not found";
    private const string PatientNotFoundMessage = "Patient not found";
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";

    private readonly IChronicConditionService _conditionService;
    private readonly ILogger<ChronicConditionsController> _logger;

    public ChronicConditionsController(
        IChronicConditionService conditionService,
        ILogger<ChronicConditionsController> logger)
    {
        _conditionService = conditionService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ChronicConditionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConditions(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        if (RejectIfRoleNotIn("Doctor", "Receptionist", "Admin") is { } forbidden)
        {
            return forbidden;
        }

        var conditions = await _conditionService.GetConditionsAsync(patientId, cancellationToken);
        return conditions is null
            ? NotFound(new MessageResponse(PatientNotFoundMessage))
            : Ok(conditions);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ChronicConditionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddCondition(
        Guid patientId,
        [FromBody] ChronicConditionRequest request,
        CancellationToken cancellationToken)
    {
        if (RejectIfRoleNotIn("Receptionist") is { } forbidden)
        {
            return forbidden;
        }

        var condition = await _conditionService.AddConditionAsync(
            patientId,
            request,
            ParseUserIdHeader(),
            cancellationToken);

        return condition is null
            ? NotFound(new MessageResponse(PatientNotFoundMessage))
            : StatusCode(StatusCodes.Status201Created, condition);
    }

    [HttpDelete("{conditionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveCondition(
        Guid patientId,
        Guid conditionId,
        CancellationToken cancellationToken)
    {
        if (RejectIfRoleNotIn("Receptionist") is { } forbidden)
        {
            return forbidden;
        }

        var removed = await _conditionService.RemoveConditionAsync(
            patientId,
            conditionId,
            ParseUserIdHeader(),
            cancellationToken);

        return removed
            ? NoContent()
            : NotFound(new MessageResponse(ConditionNotFoundMessage));
    }

    private IActionResult? RejectIfRoleNotIn(params string[] allowedRoles)
    {
        var role = HttpContext.Request.Headers[UserRoleHeaderName].FirstOrDefault();
        if (role is not null && allowedRoles.Contains(role))
        {
            return null;
        }

        _logger.LogWarning(
            "Rejected chronic-condition request from a disallowed role: userId={UserId}",
            ParseUserIdHeader());

        return StatusCode(StatusCodes.Status403Forbidden, new MessageResponse(ForbiddenMessage));
    }

    private Guid ParseUserIdHeader()
    {
        var userIdHeader = HttpContext.Request.Headers[UserIdHeaderName].FirstOrDefault();
        return Guid.TryParse(userIdHeader, out var userId) ? userId : Guid.Empty;
    }
}
