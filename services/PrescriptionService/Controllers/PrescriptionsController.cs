using Microsoft.AspNetCore.Mvc;
using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Services;

namespace PrescriptionService.Controllers;

[ApiController]
[Route("api/prescriptions")]
public sealed class PrescriptionsController(IPrescriptionService prescriptionService)
    : ControllerBase
{
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";
    private const string UserNameHeaderName = "X-User-Name";

    [HttpGet("patient/{patientId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<PrescriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPatientHistory(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        if (!IsDoctorRequest())
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new MessageResponse("Forbidden"));
        }

        var prescriptions = await prescriptionService.GetForPatientAsync(
            patientId,
            cancellationToken);

        return Ok(prescriptions);
    }

    [HttpPost]
    [ProducesResponseType(typeof(PrescriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePrescription(
        [FromBody] CreatePrescriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsDoctorRequest())
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new MessageResponse("Forbidden"));
        }

        var userIdHeader = Request.Headers[UserIdHeaderName].FirstOrDefault();
        var doctorName = Request.Headers[UserNameHeaderName].FirstOrDefault();
        if (!Guid.TryParse(userIdHeader, out var doctorId)
            || doctorId == Guid.Empty
            || string.IsNullOrWhiteSpace(doctorName))
        {
            return Unauthorized(new MessageResponse("Doctor identity is unavailable"));
        }

        var result = await prescriptionService.CreateAsync(
            request,
            doctorId,
            doctorName,
            cancellationToken);

        return result.Outcome switch
        {
            CreatePrescriptionOutcome.Success when result.Prescription is not null =>
                StatusCode(StatusCodes.Status201Created, result.Prescription),
            CreatePrescriptionOutcome.Success => throw new InvalidOperationException(
                "A successful prescription result must contain the prescription."),
            CreatePrescriptionOutcome.ConsultationAlreadyHasPrescription => Conflict(
                new MessageResponse("A prescription already exists for this consultation")),
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                result.Outcome,
                "Unsupported create-prescription outcome.")
        };
    }

    private bool IsDoctorRequest() => string.Equals(
        Request.Headers[UserRoleHeaderName].FirstOrDefault(),
        "Doctor",
        StringComparison.Ordinal);
}
