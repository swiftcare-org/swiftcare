using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PrescriptionService.Controllers;
using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Services;

namespace PrescriptionService.UnitTests.Controllers;

// SWC-130: POST /api/consultations/{consultationId}/no-prescription is for doctors only.
public class NoPrescriptionControllerTests
{
    private static readonly Guid ConsultationId = Guid.NewGuid();
    private static readonly Guid DoctorId = Guid.NewGuid();

    [Fact]
    public async Task RecordReadsTheConsultationFromTheRouteAndTheDoctorFromTrustedHeaders()
    {
        var request = ValidRequest();
        var expected = Decision(request);
        var service = new Mock<INoPrescriptionService>(MockBehavior.Strict);
        service.Setup(candidate => candidate.RecordAsync(
                ConsultationId,
                request,
                DoctorId,
                "Dr. Amara Chen",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecordNoPrescriptionResult(RecordNoPrescriptionOutcome.Recorded, expected));
        var controller = CreateController(service, "Doctor", DoctorId, "Dr. Amara Chen");

        var result = await controller.RecordNoPrescription(ConsultationId, request, CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.Same(expected, response.Value);
        service.VerifyAll();
    }

    [Theory]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    [InlineData("doctor")]
    [InlineData(null)]
    public async Task RecordAsAnyoneButADoctorReturnsForbiddenAndNeverCallsTheService(string? role)
    {
        var service = new Mock<INoPrescriptionService>(MockBehavior.Strict);
        var controller = CreateController(service, role, DoctorId, "Staff Member");

        var result = await controller.RecordNoPrescription(ConsultationId, ValidRequest(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, "Dr. Amara Chen")]
    [InlineData("not-a-guid", "Dr. Amara Chen")]
    [InlineData("00000000-0000-0000-0000-000000000000", "Dr. Amara Chen")]
    [InlineData("4d8f6f0e-3b0f-4b5a-9d6e-7f1c2a9b8c11", null)]
    [InlineData("4d8f6f0e-3b0f-4b5a-9d6e-7f1c2a9b8c11", "   ")]
    public async Task RecordWithoutAUsableDoctorIdentityReturnsUnauthorized(string? userId, string? doctorName)
    {
        var service = new Mock<INoPrescriptionService>(MockBehavior.Strict);
        var controller = CreateController(service, "Doctor", userId, doctorName);

        var result = await controller.RecordNoPrescription(ConsultationId, ValidRequest(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status401Unauthorized, "Doctor identity is unavailable");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecordWithTheAllZeroConsultationIdReturnsBadRequestAndNeverCallsTheService()
    {
        var service = new Mock<INoPrescriptionService>(MockBehavior.Strict);
        var controller = CreateController(service, "Doctor", DoctorId, "Dr. Amara Chen");

        var result = await controller.RecordNoPrescription(Guid.Empty, ValidRequest(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status400BadRequest, "Consultation ID must be provided.");
        service.VerifyNoOtherCalls();
    }

    // An empty ID must not let a caller skip the role check.
    [Fact]
    public async Task EmptyConsultationIdDoesNotBypassTheRoleCheck()
    {
        var service = new Mock<INoPrescriptionService>(MockBehavior.Strict);
        var controller = CreateController(service, "Receptionist", DoctorId, "Front Desk");

        var result = await controller.RecordNoPrescription(Guid.Empty, ValidRequest(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
    }

    [Theory]
    [InlineData(
        RecordNoPrescriptionOutcome.ConsultationAlreadyHasPrescription,
        "A prescription already exists for this consultation")]
    [InlineData(
        RecordNoPrescriptionOutcome.AlreadyRecorded,
        "No prescription required is already recorded for this consultation")]
    public async Task RejectedDecisionReturnsConflictWithItsOwnMessage(
        RecordNoPrescriptionOutcome outcome,
        string expectedMessage)
    {
        var controller = CreateController(ServiceReturning(new RecordNoPrescriptionResult(outcome)));

        var result = await controller.RecordNoPrescription(ConsultationId, ValidRequest(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status409Conflict, expectedMessage);
    }

    // A "Recorded" outcome with no decision is a programming error, not a client error.
    [Fact]
    public async Task RecordedOutcomeWithoutADecisionIsNotReportedAsSuccess()
    {
        var controller = CreateController(
            ServiceReturning(new RecordNoPrescriptionResult(RecordNoPrescriptionOutcome.Recorded)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.RecordNoPrescription(ConsultationId, ValidRequest(), CancellationToken.None));

        Assert.Equal("A recorded result must contain the decision.", exception.Message);
    }

    [Fact]
    public async Task UnknownOutcomeIsNotSilentlyTreatedAsSuccess()
    {
        var controller = CreateController(
            ServiceReturning(new RecordNoPrescriptionResult((RecordNoPrescriptionOutcome)99)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.RecordNoPrescription(ConsultationId, ValidRequest(), CancellationToken.None));

        Assert.Equal("Unsupported record-no-prescription outcome: 99.", exception.Message);
    }

    private static Mock<INoPrescriptionService> ServiceReturning(RecordNoPrescriptionResult result)
    {
        var service = new Mock<INoPrescriptionService>();
        service.Setup(candidate => candidate.RecordAsync(
                It.IsAny<Guid>(),
                It.IsAny<RecordNoPrescriptionRequest>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return service;
    }

    private static Caller CreateController(Mock<INoPrescriptionService> service) =>
        CreateController(service, "Doctor", DoctorId, "Dr. Amara Chen");

    private static Caller CreateController(
        Mock<INoPrescriptionService> service,
        string? role,
        Guid doctorId,
        string? doctorName) => CreateController(service, role, doctorId.ToString(), doctorName);

    private static Caller CreateController(
        Mock<INoPrescriptionService> service,
        string? role,
        string? userId,
        string? doctorName) => new(new NoPrescriptionController(service.Object), role, userId, doctorName);

    // Calls the action the way the framework does: the identity headers the Gateway sets
    // arrive as bound parameters.
    private sealed class Caller(
        NoPrescriptionController controller,
        string? role,
        string? userId,
        string? doctorName)
    {
        public Task<IActionResult> RecordNoPrescription(
            Guid consultationId,
            RecordNoPrescriptionRequest request,
            CancellationToken cancellationToken) =>
            controller.RecordNoPrescription(consultationId, request, role, userId, doctorName, cancellationToken);
    }

    private static void AssertMessage(IActionResult result, int expectedStatus, string expectedMessage)
    {
        var response = Assert.IsType<ObjectResult>(result, exactMatch: false);
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedMessage, Assert.IsType<MessageResponse>(response.Value).Message);
    }

    private static RecordNoPrescriptionRequest ValidRequest() => new()
    {
        QueueId = Guid.NewGuid(),
        PatientId = Guid.NewGuid()
    };

    private static PrescriptionResponse Decision(RecordNoPrescriptionRequest request) => new(
        Guid.NewGuid(),
        ConsultationId,
        request.QueueId!.Value,
        request.PatientId!.Value,
        DoctorId,
        "Dr. Amara Chen",
        "NOT_REQUIRED",
        new DateTime(2026, 10, 7, 4, 30, 0, DateTimeKind.Utc),
        []);
}
