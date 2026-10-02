using MedicalRecordService.Controllers;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace MedicalRecordService.UnitTests.Controllers;

public class PatientHistoryControllerTests
{
    [Fact]
    public async Task ConsultationHistoryAsDoctorReturnsConsultations()
    {
        var patientId = Guid.NewGuid();
        IReadOnlyList<ConsultationResponse> history = [CreateConsultation(patientId)];
        var service = new Mock<IConsultationHistoryService>(MockBehavior.Strict);
        service.Setup(item => item.GetHistoryAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);

        var result = await CreateConsultationController(service, "Doctor", Guid.NewGuid().ToString())
            .GetHistory(patientId, CancellationToken.None);

        Assert.Same(history, Assert.IsType<OkObjectResult>(result).Value);
        service.VerifyAll();
    }

    [Fact]
    public async Task ConsultationHistoryReturnsEmptyListForFirstVisit()
    {
        var patientId = Guid.NewGuid();
        var service = new Mock<IConsultationHistoryService>(MockBehavior.Strict);
        service.Setup(item => item.GetHistoryAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateConsultationController(service, "Doctor", Guid.NewGuid().ToString())
            .GetHistory(patientId, CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<ConsultationResponse>>(response.Value));
    }

    [Fact]
    public async Task LatestConsultationAsDoctorReturnsConsultation()
    {
        var patientId = Guid.NewGuid();
        var latest = CreateConsultation(patientId);
        var service = new Mock<IConsultationHistoryService>(MockBehavior.Strict);
        service.Setup(item => item.GetLatestAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latest);

        var result = await CreateConsultationController(service, "Doctor", Guid.NewGuid().ToString())
            .GetLatest(patientId, CancellationToken.None);

        Assert.Same(latest, Assert.IsType<OkObjectResult>(result).Value);
        service.VerifyAll();
    }

    [Fact]
    public async Task LatestConsultationReturnsNoContentForFirstVisit()
    {
        var patientId = Guid.NewGuid();
        var service = new Mock<IConsultationHistoryService>(MockBehavior.Strict);
        service.Setup(item => item.GetLatestAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsultationResponse?)null);

        var result = await CreateConsultationController(service, "Doctor", Guid.NewGuid().ToString())
            .GetLatest(patientId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        service.VerifyAll();
    }

    [Theory]
    [InlineData("Receptionist", "invalid", 403)]
    [InlineData("Admin", "invalid", 403)]
    [InlineData("Doctor", "invalid", 401)]
    public async Task ConsultationEndpointsRejectUntrustedIdentityBeforeCallingService(
        string role, string userId, int expectedStatus)
    {
        var service = new Mock<IConsultationHistoryService>(MockBehavior.Strict);
        var controller = CreateConsultationController(service, role, userId);

        var history = await controller.GetHistory(Guid.NewGuid(), CancellationToken.None);
        var latest = await controller.GetLatest(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<ObjectResult>(history).StatusCode);
        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<ObjectResult>(latest).StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VitalsHistoryAsDoctorReturnsReadings()
    {
        var patientId = Guid.NewGuid();
        IReadOnlyList<VitalSignsResponse> history =
        [
            new()
            {
                Id = Guid.NewGuid(),
                ConsultationId = Guid.NewGuid(),
                PulseRate = 72,
                RecordedAt = new DateTime(2026, 9, 21, 4, 0, 0, DateTimeKind.Utc)
            }
        ];
        var service = new Mock<IVitalSignsHistoryService>(MockBehavior.Strict);
        service.Setup(item => item.GetHistoryAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);

        var result = await CreateVitalsController(service, "Doctor", Guid.NewGuid().ToString())
            .GetHistory(patientId, CancellationToken.None);

        Assert.Same(history, Assert.IsType<OkObjectResult>(result).Value);
        service.VerifyAll();
    }

    [Theory]
    [InlineData("Receptionist", "invalid", 403)]
    [InlineData("Admin", "invalid", 403)]
    [InlineData("Doctor", "invalid", 401)]
    public async Task VitalsHistoryRejectsUntrustedIdentityBeforeCallingService(
        string role, string userId, int expectedStatus)
    {
        var service = new Mock<IVitalSignsHistoryService>(MockBehavior.Strict);

        var result = await CreateVitalsController(service, role, userId)
            .GetHistory(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        service.VerifyNoOtherCalls();
    }

    // SWC-147: the all-zero GUID passes the route constraint, so the controllers reject it
    // with 400 instead of letting the service's guard surface as a 500.
    [Fact]
    public async Task ConsultationEndpointsRejectEmptyPatientIdBeforeCallingService()
    {
        var service = new Mock<IConsultationHistoryService>(MockBehavior.Strict);
        var controller = CreateConsultationController(service, "Doctor", Guid.NewGuid().ToString());

        var history = await controller.GetHistory(Guid.Empty, CancellationToken.None);
        var latest = await controller.GetLatest(Guid.Empty, CancellationToken.None);

        AssertMissingPatientId(history);
        AssertMissingPatientId(latest);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VitalsHistoryRejectsEmptyPatientIdBeforeCallingService()
    {
        var service = new Mock<IVitalSignsHistoryService>(MockBehavior.Strict);

        var result = await CreateVitalsController(service, "Doctor", Guid.NewGuid().ToString())
            .GetHistory(Guid.Empty, CancellationToken.None);

        AssertMissingPatientId(result);
        service.VerifyNoOtherCalls();
    }

    // Authorization still wins: a caller who may not use the endpoint learns nothing
    // about how the patient ID was judged.
    [Theory]
    [InlineData("Receptionist", "invalid", 403)]
    [InlineData("Doctor", "invalid", 401)]
    public async Task EmptyPatientIdDoesNotBypassIdentityChecks(string role, string userId, int expectedStatus)
    {
        var consultations = new Mock<IConsultationHistoryService>(MockBehavior.Strict);
        var vitals = new Mock<IVitalSignsHistoryService>(MockBehavior.Strict);

        var history = await CreateConsultationController(consultations, role, userId)
            .GetHistory(Guid.Empty, CancellationToken.None);
        var readings = await CreateVitalsController(vitals, role, userId)
            .GetHistory(Guid.Empty, CancellationToken.None);

        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<ObjectResult>(history).StatusCode);
        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<ObjectResult>(readings).StatusCode);
    }

    private static void AssertMissingPatientId(IActionResult result)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(
            "Patient ID must be provided.",
            Assert.IsType<MessageResponse>(badRequest.Value).Message);
    }

    private static ConsultationResponse CreateConsultation(Guid patientId) => new()
    {
        Id = Guid.NewGuid(),
        QueueId = Guid.NewGuid(),
        PatientId = patientId,
        DoctorId = Guid.NewGuid(),
        DoctorName = "Dr. Silva",
        RoomNumber = "R-204",
        Symptoms = "Headache",
        Diagnosis = "Hypertension",
        ConsultationDate = new DateTime(2026, 9, 21, 4, 0, 0, DateTimeKind.Utc)
    };

    private static ConsultationHistoryController CreateConsultationController(
        Mock<IConsultationHistoryService> service, string role, string userId)
    {
        var controller = new ConsultationHistoryController(service.Object);
        SetIdentity(controller, role, userId);
        return controller;
    }

    private static VitalSignsHistoryController CreateVitalsController(
        Mock<IVitalSignsHistoryService> service, string role, string userId)
    {
        var controller = new VitalSignsHistoryController(service.Object);
        SetIdentity(controller, role, userId);
        return controller;
    }

    private static void SetIdentity(ControllerBase controller, string role, string userId)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Role"] = role;
        context.Request.Headers["X-User-Id"] = userId;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
    }
}
