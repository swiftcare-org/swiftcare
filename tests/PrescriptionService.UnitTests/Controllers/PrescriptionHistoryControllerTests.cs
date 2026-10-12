using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PrescriptionService.Controllers;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Services;

namespace PrescriptionService.UnitTests.Controllers;

// SWC-142: prescription history and empty history.
public class PrescriptionHistoryControllerTests
{
    [Fact]
    public async Task PatientHistoryAsDoctorReturnsPrescriptionsInServiceOrder()
    {
        var patientId = Guid.NewGuid();
        IReadOnlyList<PrescriptionResponse> history =
        [
            Prescription(patientId, new DateTime(2026, 10, 2, 4, 0, 0, DateTimeKind.Utc), "PENDING"),
            Prescription(patientId, new DateTime(2026, 9, 20, 4, 0, 0, DateTimeKind.Utc), "DISPENSED")
        ];
        var service = new Mock<IPrescriptionService>(MockBehavior.Strict);
        service.Setup(item => item.GetForPatientAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);

        var result = await CreateHistoryController(service, "Doctor")
            .GetPatientHistory(patientId, CancellationToken.None);

        Assert.Same(history, Assert.IsType<OkObjectResult>(result).Value);
        service.VerifyAll();
    }

    [Fact]
    public async Task PatientHistoryReturnsEmptyListWhenPatientHasNoPrescriptions()
    {
        var patientId = Guid.NewGuid();
        var service = new Mock<IPrescriptionService>(MockBehavior.Strict);
        service.Setup(item => item.GetForPatientAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateHistoryController(service, "Doctor")
            .GetPatientHistory(patientId, CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<PrescriptionResponse>>(response.Value));
    }

    [Theory]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task PatientHistoryAsNonDoctorReturnsForbidden(string role)
    {
        var service = new Mock<IPrescriptionService>(MockBehavior.Strict);

        var result = await CreateHistoryController(service, role)
            .GetPatientHistory(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            Assert.IsType<ObjectResult>(result).StatusCode);
        service.VerifyNoOtherCalls();
    }

    // The all-zero GUID passes the route constraint, so it is rejected here instead of
    // reaching the service guard and surfacing as a 500 (the SWC-147 defect).
    [Fact]
    public async Task PatientHistoryRejectsEmptyPatientIdBeforeCallingService()
    {
        var service = new Mock<IPrescriptionService>(MockBehavior.Strict);

        var result = await CreateHistoryController(service, "Doctor")
            .GetPatientHistory(Guid.Empty, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(
            "Patient ID must be provided.",
            Assert.IsType<MessageResponse>(badRequest.Value).Message);
        service.VerifyNoOtherCalls();
    }

    private static PrescriptionsController CreateHistoryController(
        Mock<IPrescriptionService> service,
        string role) => WithRole(new PrescriptionsController(service.Object), role);

    private static TController WithRole<TController>(TController controller, string role)
        where TController : ControllerBase
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Role"] = role;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private static PrescriptionResponse Prescription(Guid patientId, DateTime createdAt, string status) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            patientId,
            Guid.NewGuid(),
            "Dr. Amara Chen",
            status,
            createdAt,
            []);
}
