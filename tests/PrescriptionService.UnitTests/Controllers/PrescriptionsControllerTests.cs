using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PrescriptionService.Controllers;
using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Services;

namespace PrescriptionService.UnitTests.Controllers;

public class PrescriptionsControllerTests
{
    [Fact]
    public async Task CreateReadsDoctorIdentityFromTrustedHeaders()
    {
        var request = ValidRequest();
        var doctorId = Guid.NewGuid();
        var expected = ExpectedResponse(request, doctorId);
        var service = new Mock<IPrescriptionService>();
        service.Setup(candidate => candidate.CreateAsync(
                request,
                doctorId,
                "Dr. Amara Chen",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatePrescriptionResult(
                CreatePrescriptionOutcome.Success,
                expected));
        var controller = CreateController(service, "Doctor", doctorId, "Dr. Amara Chen");

        var result = await controller.CreatePrescription(request, CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.Same(expected, response.Value);
        service.VerifyAll();
    }

    [Theory]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task CreateAsNonDoctorReturnsForbidden(string role)
    {
        var service = new Mock<IPrescriptionService>();
        var controller = CreateController(service, role, Guid.NewGuid(), "Staff Member");

        var result = await controller.CreatePrescription(
            ValidRequest(),
            CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateWithoutDoctorIdentityReturnsUnauthorized()
    {
        var service = new Mock<IPrescriptionService>();
        var controller = CreateController(service, "Doctor");

        var result = await controller.CreatePrescription(
            ValidRequest(),
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        service.VerifyNoOtherCalls();
    }

    private static PrescriptionsController CreateController(
        Mock<IPrescriptionService> service,
        string role,
        Guid? doctorId = null,
        string? doctorName = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Role"] = role;
        if (doctorId is Guid id)
        {
            context.Request.Headers["X-User-Id"] = id.ToString();
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
        Medicines =
        [
            new PrescriptionItemRequest
            {
                MedicineName = "Amoxicillin",
                Dosage = "500 mg",
                Frequency = "Twice daily",
                Duration = "5 days"
            }
        ]
    };

    private static PrescriptionResponse ExpectedResponse(
        CreatePrescriptionRequest request,
        Guid doctorId) => new(
            Guid.NewGuid(),
            request.ConsultationId,
            request.QueueId,
            request.PatientId,
            doctorId,
            "Dr. Amara Chen",
            "PENDING",
            new DateTime(2026, 9, 23, 8, 30, 0, DateTimeKind.Utc),
            []);
}
