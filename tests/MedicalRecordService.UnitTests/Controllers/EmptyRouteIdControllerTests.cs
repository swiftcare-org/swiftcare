using MedicalRecordService.Controllers;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace MedicalRecordService.UnitTests.Controllers;

// A "{id:guid}" route constraint accepts the all-zero GUID. These endpoints reject it
// with 400 before the service is called, so its guard cannot surface as an HTTP 500.
public class EmptyRouteIdControllerTests
{
    [Fact]
    public async Task LatestFollowUpRejectsEmptyPatientId()
    {
        var service = new Mock<IConsultationFollowUpService>(MockBehavior.Strict);
        var controller = WithDoctor(new ConsultationFollowUpController(service.Object));

        var result = await controller.GetLatestOverdue(Guid.Empty, CancellationToken.None);

        AssertBadRequest(result, "Patient ID must be provided.");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecordVitalSignsRejectsEmptyConsultationId()
    {
        var service = new Mock<IVitalSignsService>(MockBehavior.Strict);
        var controller = WithDoctor(new ConsultationVitalSignsController(service.Object));

        var result = await controller.RecordVitalSigns(
            Guid.Empty, new RecordVitalSignsRequest(), CancellationToken.None);

        AssertBadRequest(result, "Consultation ID must be provided.");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompleteConsultationRejectsEmptyConsultationId()
    {
        var service = new Mock<IConsultationCompletionService>(MockBehavior.Strict);
        var controller = WithDoctor(new ConsultationCompletionController(service.Object));

        var result = await controller.Complete(Guid.Empty, CancellationToken.None);

        AssertBadRequest(result, "Consultation ID must be provided.");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConsultationForQueueRejectsEmptyQueueId()
    {
        var service = new Mock<IConsultationCompletionService>(MockBehavior.Strict);
        var controller = WithDoctor(new ConsultationProgressController(service.Object));

        var result = await controller.GetForQueue(Guid.Empty, CancellationToken.None);

        AssertBadRequest(result, "Queue ID must be provided.");
        service.VerifyNoOtherCalls();
    }

    // Authorization still wins: a caller who may not use the endpoint gets 403, not 400.
    [Fact]
    public async Task EmptyIdDoesNotBypassTheRoleCheck()
    {
        var followUps = new Mock<IConsultationFollowUpService>(MockBehavior.Strict);
        var controller = WithRole(
            new ConsultationFollowUpController(followUps.Object), "Receptionist", Guid.NewGuid().ToString());

        var result = await controller.GetLatestOverdue(Guid.Empty, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    private static void AssertBadRequest(IActionResult result, string expectedMessage)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(expectedMessage, Assert.IsType<MessageResponse>(badRequest.Value).Message);
    }

    private static TController WithDoctor<TController>(TController controller)
        where TController : ControllerBase => WithRole(controller, "Doctor", Guid.NewGuid().ToString());

    private static TController WithRole<TController>(TController controller, string role, string userId)
        where TController : ControllerBase
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Role"] = role;
        context.Request.Headers["X-User-Id"] = userId;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }
}
