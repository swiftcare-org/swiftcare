using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PrescriptionService.Controllers;
using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Services;

namespace PrescriptionService.UnitTests.Controllers;

// Role checks, identity headers and outcome mappings of PrescriptionsController that the
// original suite did not reach or did not assert on (SWC-151 mutation testing).
public class PrescriptionsControllerEdgeCaseTests
{
    private static readonly Guid DoctorId = Guid.NewGuid();

    [Fact]
    public async Task GetPendingAsDoctorIsForbiddenWithMessage()
    {
        var service = new Mock<IPrescriptionService>();

        var result = await CreateController(service, "Doctor").GetPending(CancellationToken.None);

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PatientHistoryAsReceptionistIsForbiddenWithMessage()
    {
        var service = new Mock<IPrescriptionService>();

        var result = await CreateController(service, "Receptionist")
            .GetPatientHistory(Guid.NewGuid(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Patient")]
    [InlineData("doctor")]
    [InlineData(null)]
    public async Task GetByQueueIdForUnsupportedOrMissingRoleIsForbidden(string? role)
    {
        var service = new Mock<IPrescriptionService>();

        var result = await CreateController(service, role)
            .GetByQueueId(Guid.NewGuid(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetByQueueIdReturnsNotFoundWhenQueueHasNoPrescription()
    {
        var queueId = Guid.NewGuid();
        var service = new Mock<IPrescriptionService>();
        service.Setup(candidate => candidate.GetByQueueIdAsync(queueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PrescriptionResponse?)null);

        var result = await CreateController(service, "Receptionist")
            .GetByQueueId(queueId, CancellationToken.None);

        AssertMessage(result, StatusCodes.Status404NotFound, "Prescription was not found");
    }

    [Theory]
    [InlineData("Receptionist")]
    [InlineData(null)]
    public async Task CreateForNonDoctorOrMissingRoleIsForbiddenWithMessage(string? role)
    {
        var service = new Mock<IPrescriptionService>();

        var result = await CreateController(service, role, DoctorId.ToString(), "Dr. Amara Chen")
            .CreatePrescription(ValidRequest(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("not-a-guid", "Dr. Amara Chen")]
    [InlineData("00000000-0000-0000-0000-000000000000", "Dr. Amara Chen")]
    [InlineData("11111111-1111-1111-1111-111111111111", "   ")]
    [InlineData("11111111-1111-1111-1111-111111111111", null)]
    public async Task CreateWithIncompleteDoctorIdentityIsUnauthorized(string userId, string? doctorName)
    {
        var service = new Mock<IPrescriptionService>();

        var result = await CreateController(service, "Doctor", userId, doctorName)
            .CreatePrescription(ValidRequest(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status401Unauthorized, "Doctor identity is unavailable");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateForConsultationThatAlreadyHasPrescriptionReturnsConflict()
    {
        var service = ServiceReturning(new CreatePrescriptionResult(
            CreatePrescriptionOutcome.ConsultationAlreadyHasPrescription));

        var result = await CreateDoctorController(service)
            .CreatePrescription(ValidRequest(), CancellationToken.None);

        AssertMessage(
            result,
            StatusCodes.Status409Conflict,
            "A prescription already exists for this consultation");
    }

    [Fact]
    public async Task CreateSuccessWithoutPrescriptionIsAnInvariantViolation()
    {
        var service = ServiceReturning(new CreatePrescriptionResult(CreatePrescriptionOutcome.Success));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateDoctorController(service).CreatePrescription(ValidRequest(), CancellationToken.None));

        Assert.Equal("A successful prescription result must contain the prescription.", exception.Message);
    }

    [Fact]
    public async Task CreateWithUnknownOutcomeIsRejected()
    {
        var service = ServiceReturning(new CreatePrescriptionResult((CreatePrescriptionOutcome)99));

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CreateDoctorController(service).CreatePrescription(ValidRequest(), CancellationToken.None));

        Assert.StartsWith("Unsupported create-prescription outcome.", exception.Message);
    }

    [Fact]
    public async Task AddMedicineForNonDoctorIsForbiddenWithMessage()
    {
        var service = new Mock<IPrescriptionService>();

        var result = await CreateController(service, "Receptionist", DoctorId.ToString())
            .AddMedicine(Guid.NewGuid(), Medicine(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoveMedicineForNonDoctorIsForbiddenWithMessage()
    {
        var service = new Mock<IPrescriptionService>();

        var result = await CreateController(service, "Receptionist", DoctorId.ToString())
            .RemoveMedicine(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task MedicineChangesWithoutValidDoctorIdAreUnauthorized(string? userId)
    {
        var service = new Mock<IPrescriptionService>();
        var controller = CreateController(service, "Doctor", userId, "Dr. Amara Chen");

        var added = await controller.AddMedicine(Guid.NewGuid(), Medicine(), CancellationToken.None);
        var removed = await controller.RemoveMedicine(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        AssertMessage(added, StatusCodes.Status401Unauthorized, "Doctor identity is unavailable");
        AssertMessage(removed, StatusCodes.Status401Unauthorized, "Doctor identity is unavailable");
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(PrescriptionItemChangeOutcome.PrescriptionNotFound, StatusCodes.Status404NotFound, "Prescription was not found")]
    [InlineData(PrescriptionItemChangeOutcome.MedicineNotFound, StatusCodes.Status404NotFound, "Medicine was not found")]
    [InlineData(PrescriptionItemChangeOutcome.MinimumOneMedicineRequired, StatusCodes.Status409Conflict, "Prescription must have at least one medicine")]
    [InlineData(PrescriptionItemChangeOutcome.PrescriptionDispensed, StatusCodes.Status409Conflict, "Cannot modify a dispensed prescription")]
    [InlineData(PrescriptionItemChangeOutcome.ConcurrentModification, StatusCodes.Status409Conflict, "Prescription changed. Reload before retrying")]
    public async Task AddMedicineMapsEveryFailureOutcome(
        PrescriptionItemChangeOutcome outcome,
        int statusCode,
        string message)
    {
        var service = new Mock<IPrescriptionService>();
        service.Setup(candidate => candidate.AddMedicineAsync(
                It.IsAny<Guid>(),
                It.IsAny<PrescriptionItemRequest>(),
                DoctorId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrescriptionItemChangeResult(outcome));

        var result = await CreateDoctorController(service)
            .AddMedicine(Guid.NewGuid(), Medicine(), CancellationToken.None);

        AssertMessage(result, statusCode, message);
    }

    [Fact]
    public async Task MedicineChangeSuccessWithoutPrescriptionIsAnInvariantViolation()
    {
        var service = new Mock<IPrescriptionService>();
        service.Setup(candidate => candidate.RemoveMedicineAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                DoctorId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrescriptionItemChangeResult(PrescriptionItemChangeOutcome.Success));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateDoctorController(service).RemoveMedicine(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("A successful prescription item change must contain the prescription.", exception.Message);
    }

    [Fact]
    public async Task MedicineChangeWithUnknownOutcomeIsRejected()
    {
        var service = new Mock<IPrescriptionService>();
        service.Setup(candidate => candidate.RemoveMedicineAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                DoctorId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrescriptionItemChangeResult((PrescriptionItemChangeOutcome)99));

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CreateDoctorController(service).RemoveMedicine(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));

        Assert.StartsWith("Unsupported prescription item change outcome.", exception.Message);
    }

    [Fact]
    public async Task DispenseAsDoctorIsForbiddenWithMessage()
    {
        var service = new Mock<IPrescriptionService>();

        var result = await CreateController(service, "Doctor", doctorName: "Dr. Amara Chen")
            .DispensePrescription(Guid.NewGuid(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task DispenseWithoutReceptionistNameIsUnauthorizedWithMessage(string? receptionistName)
    {
        var service = new Mock<IPrescriptionService>();

        var result = await CreateController(service, "Receptionist", doctorName: receptionistName)
            .DispensePrescription(Guid.NewGuid(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status401Unauthorized, "Receptionist identity is unavailable");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DispenseUnknownPrescriptionReturnsNotFound()
    {
        var service = DispenseServiceReturning(
            new DispensePrescriptionResult(DispensePrescriptionOutcome.PrescriptionNotFound));

        var result = await CreateReceptionistController(service)
            .DispensePrescription(Guid.NewGuid(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status404NotFound, "Prescription was not found");
    }

    [Theory]
    [InlineData(DispensePrescriptionOutcome.AlreadyDispensed, "Prescription has already been dispensed")]
    [InlineData(DispensePrescriptionOutcome.ConcurrentModification, "Prescription changed. Reload before retrying")]
    public async Task DispenseConflictReturnsMessage(DispensePrescriptionOutcome outcome, string message)
    {
        var service = DispenseServiceReturning(
            new DispensePrescriptionResult(outcome));

        var result = await CreateReceptionistController(service)
            .DispensePrescription(Guid.NewGuid(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status409Conflict, message);
    }

    [Fact]
    public async Task DispenseSuccessWithoutPrescriptionIsAnInvariantViolation()
    {
        var service = DispenseServiceReturning(
            new DispensePrescriptionResult(DispensePrescriptionOutcome.Success));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateReceptionistController(service).DispensePrescription(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("A successful dispense result must contain the prescription.", exception.Message);
    }

    [Fact]
    public async Task DispenseWithUnknownOutcomeIsRejected()
    {
        var service = DispenseServiceReturning(
            new DispensePrescriptionResult((DispensePrescriptionOutcome)99));

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CreateReceptionistController(service).DispensePrescription(Guid.NewGuid(), CancellationToken.None));

        Assert.StartsWith("Unsupported dispense-prescription outcome.", exception.Message);
    }

    private static void AssertMessage(IActionResult result, int statusCode, string message)
    {
        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal(message, Assert.IsType<MessageResponse>(response.Value).Message);
    }

    private static Mock<IPrescriptionService> ServiceReturning(CreatePrescriptionResult result)
    {
        var service = new Mock<IPrescriptionService>();
        service.Setup(candidate => candidate.CreateAsync(
                It.IsAny<CreatePrescriptionRequest>(),
                DoctorId,
                "Dr. Amara Chen",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return service;
    }

    private static Mock<IPrescriptionService> DispenseServiceReturning(DispensePrescriptionResult result)
    {
        var service = new Mock<IPrescriptionService>();
        service.Setup(candidate => candidate.DispenseAsync(
                It.IsAny<Guid>(),
                "Nimali Perera",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return service;
    }

    private static PrescriptionsController CreateDoctorController(Mock<IPrescriptionService> service) =>
        CreateController(service, "Doctor", DoctorId.ToString(), "Dr. Amara Chen");

    private static PrescriptionsController CreateReceptionistController(Mock<IPrescriptionService> service) =>
        CreateController(service, "Receptionist", doctorName: "Nimali Perera");

    private static PrescriptionsController CreateController(
        Mock<IPrescriptionService> service,
        string? role,
        string? userId = null,
        string? doctorName = null)
    {
        var context = new DefaultHttpContext();
        if (role is not null)
        {
            context.Request.Headers["X-User-Role"] = role;
        }

        if (userId is not null)
        {
            context.Request.Headers["X-User-Id"] = userId;
        }

        if (doctorName is not null)
        {
            context.Request.Headers["X-User-Name"] = doctorName;
        }

        return new PrescriptionsController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static CreatePrescriptionRequest ValidRequest() => new()
    {
        ConsultationId = Guid.NewGuid(),
        QueueId = Guid.NewGuid(),
        PatientId = Guid.NewGuid(),
        Medicines = [Medicine()]
    };

    private static PrescriptionItemRequest Medicine() => new()
    {
        MedicineName = "Amoxicillin",
        Dosage = "500 mg",
        Frequency = "Twice daily",
        Duration = "5 days"
    };
}
