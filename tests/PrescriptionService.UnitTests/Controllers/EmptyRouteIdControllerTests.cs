using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PrescriptionService.Controllers;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Services;

namespace PrescriptionService.UnitTests.Controllers;

// A "{id:guid}" route constraint accepts the all-zero GUID. These endpoints reject it
// with 400 before the service is called, so its guard cannot surface as an HTTP 500.
public class EmptyRouteIdControllerTests
{
    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task PrescriptionForQueueRejectsEmptyQueueId(string role)
    {
        var service = new Mock<IPrescriptionService>(MockBehavior.Strict);

        var result = await CreateController(service, role)
            .GetByQueueId(Guid.Empty, CancellationToken.None);

        AssertBadRequest(result, "Queue ID must be provided.");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddMedicineRejectsEmptyPrescriptionId()
    {
        var service = new Mock<IPrescriptionService>(MockBehavior.Strict);

        var result = await CreateController(service, "Doctor")
            .AddMedicine(Guid.Empty, new PrescriptionItemRequest(), CancellationToken.None);

        AssertBadRequest(result, "Prescription ID must be provided.");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoveMedicineRejectsEmptyPrescriptionId()
    {
        var service = new Mock<IPrescriptionService>(MockBehavior.Strict);

        var result = await CreateController(service, "Doctor")
            .RemoveMedicine(Guid.Empty, Guid.NewGuid(), CancellationToken.None);

        AssertBadRequest(result, "Prescription ID must be provided.");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoveMedicineRejectsEmptyMedicineId()
    {
        var service = new Mock<IPrescriptionService>(MockBehavior.Strict);

        var result = await CreateController(service, "Doctor")
            .RemoveMedicine(Guid.NewGuid(), Guid.Empty, CancellationToken.None);

        AssertBadRequest(result, "Medicine ID must be provided.");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DispenseRejectsEmptyPrescriptionId()
    {
        var service = new Mock<IPrescriptionService>(MockBehavior.Strict);

        var result = await CreateController(service, "Receptionist")
            .DispensePrescription(Guid.Empty, CancellationToken.None);

        AssertBadRequest(result, "Prescription ID must be provided.");
        service.VerifyNoOtherCalls();
    }

    // Authorization still wins: a caller who may not use the endpoint gets 403, not 400.
    [Fact]
    public async Task EmptyIdDoesNotBypassTheRoleCheck()
    {
        var service = new Mock<IPrescriptionService>(MockBehavior.Strict);

        var result = await CreateController(service, "Doctor")
            .DispensePrescription(Guid.Empty, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        service.VerifyNoOtherCalls();
    }

    private static void AssertBadRequest(IActionResult result, string expectedMessage)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(expectedMessage, Assert.IsType<MessageResponse>(badRequest.Value).Message);
    }

    private static PrescriptionsController CreateController(Mock<IPrescriptionService> service, string role)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Role"] = role;
        context.Request.Headers["X-User-Id"] = Guid.NewGuid().ToString();
        context.Request.Headers["X-User-Name"] = "Test User";
        return new PrescriptionsController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }
}
