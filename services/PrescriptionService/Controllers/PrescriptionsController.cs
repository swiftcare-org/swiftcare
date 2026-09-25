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

    [HttpGet("queue/{queueId:guid}")]
    [ProducesResponseType(typeof(PrescriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByQueueId(
        Guid queueId,
        CancellationToken cancellationToken)
    {
        if (!IsRoleAllowed("Doctor", "Receptionist", "Admin"))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new MessageResponse("Forbidden"));
        }

        var prescription = await prescriptionService.GetByQueueIdAsync(
            queueId,
            cancellationToken);

        return prescription is null
            ? NotFound(new MessageResponse("Prescription was not found"))
            : Ok(prescription);
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

    [HttpPost("{prescriptionId:guid}/items")]
    [ProducesResponseType(typeof(PrescriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddMedicine(
        Guid prescriptionId,
        [FromBody] PrescriptionItemRequest request,
        CancellationToken cancellationToken)
    {
        var identityError = GetDoctorIdentityError(out var doctorId);
        if (identityError is not null)
        {
            return identityError;
        }

        var result = await prescriptionService.AddMedicineAsync(
            prescriptionId,
            request,
            doctorId,
            cancellationToken);

        return ItemChangeResponse(result, StatusCodes.Status201Created);
    }

    [HttpDelete("{prescriptionId:guid}/items/{medicineId:guid}")]
    [ProducesResponseType(typeof(PrescriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveMedicine(
        Guid prescriptionId,
        Guid medicineId,
        CancellationToken cancellationToken)
    {
        var identityError = GetDoctorIdentityError(out var doctorId);
        if (identityError is not null)
        {
            return identityError;
        }

        var result = await prescriptionService.RemoveMedicineAsync(
            prescriptionId,
            medicineId,
            doctorId,
            cancellationToken);

        return ItemChangeResponse(result, StatusCodes.Status200OK);
    }

    [HttpPut("{prescriptionId:guid}/dispense")]
    [ProducesResponseType(typeof(PrescriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DispensePrescription(
        Guid prescriptionId,
        CancellationToken cancellationToken)
    {
        if (!IsRoleAllowed("Receptionist"))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new MessageResponse("Forbidden"));
        }

        var receptionistName = Request.Headers[UserNameHeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(receptionistName))
        {
            return Unauthorized(new MessageResponse("Receptionist identity is unavailable"));
        }

        var result = await prescriptionService.DispenseAsync(
            prescriptionId,
            receptionistName,
            cancellationToken);

        return result.Outcome switch
        {
            DispensePrescriptionOutcome.Success when result.Prescription is not null =>
                Ok(result.Prescription),
            DispensePrescriptionOutcome.Success => throw new InvalidOperationException(
                "A successful dispense result must contain the prescription."),
            DispensePrescriptionOutcome.PrescriptionNotFound => NotFound(
                new MessageResponse("Prescription was not found")),
            DispensePrescriptionOutcome.AlreadyDispensed => Conflict(
                new MessageResponse("Prescription has already been dispensed")),
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                result.Outcome,
                "Unsupported dispense-prescription outcome.")
        };
    }

    private IActionResult? GetDoctorIdentityError(out Guid doctorId)
    {
        doctorId = Guid.Empty;
        if (!IsDoctorRequest())
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new MessageResponse("Forbidden"));
        }

        var userIdHeader = Request.Headers[UserIdHeaderName].FirstOrDefault();
        if (!Guid.TryParse(userIdHeader, out doctorId) || doctorId == Guid.Empty)
        {
            return Unauthorized(new MessageResponse("Doctor identity is unavailable"));
        }

        return null;
    }

    private IActionResult ItemChangeResponse(
        PrescriptionItemChangeResult result,
        int successStatusCode) => result.Outcome switch
        {
            PrescriptionItemChangeOutcome.Success when result.Prescription is not null =>
                StatusCode(successStatusCode, result.Prescription),
            PrescriptionItemChangeOutcome.Success => throw new InvalidOperationException(
                "A successful prescription item change must contain the prescription."),
            PrescriptionItemChangeOutcome.PrescriptionNotFound => NotFound(
                new MessageResponse("Prescription was not found")),
            PrescriptionItemChangeOutcome.MedicineNotFound => NotFound(
                new MessageResponse("Medicine was not found")),
            PrescriptionItemChangeOutcome.MinimumOneMedicineRequired => Conflict(
                new MessageResponse("Prescription must have at least one medicine")),
            PrescriptionItemChangeOutcome.PrescriptionDispensed => Conflict(
                new MessageResponse("Cannot modify a dispensed prescription")),
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                result.Outcome,
                "Unsupported prescription item change outcome.")
        };

    private bool IsDoctorRequest() => string.Equals(
        Request.Headers[UserRoleHeaderName].FirstOrDefault(),
        "Doctor",
        StringComparison.Ordinal);

    private bool IsRoleAllowed(params string[] roles)
    {
        var role = Request.Headers[UserRoleHeaderName].FirstOrDefault();
        return role is not null && roles.Contains(role, StringComparer.Ordinal);
    }
}
