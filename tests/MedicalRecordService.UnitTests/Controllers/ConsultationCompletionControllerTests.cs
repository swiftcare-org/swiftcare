using MedicalRecordService.Controllers;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Enums;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace MedicalRecordService.UnitTests.Controllers;

public class ConsultationCompletionControllerTests
{
    [Fact]
    public async Task CompleteAsDoctorReturnsStoredEventId()
    {
        var consultationId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var service = new Mock<IConsultationCompletionService>(MockBehavior.Strict);
        service.Setup(item => item.CompleteAsync(consultationId, doctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompleteConsultationResult(CompleteConsultationOutcome.Success, eventId));

        var result = await CreateCompletionController(service, "Doctor", doctorId.ToString())
            .Complete(consultationId, CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(eventId, Assert.IsType<CompleteConsultationResponse>(response.Value).EventId);
        service.VerifyAll();
    }

    [Theory]
    [InlineData(CompleteConsultationOutcome.ConsultationNotFound, 404,
        "Consultation was not found for this doctor")]
    [InlineData(CompleteConsultationOutcome.VitalSignsMissing, 409,
        "Please save vital signs first")]
    [InlineData(CompleteConsultationOutcome.PublishFailed, 503,
        "Consultation could not be completed. Please try again.")]
    public async Task CompleteReturnsActionableResultForFailure(
        CompleteConsultationOutcome outcome, int expectedStatus, string expectedMessage)
    {
        var consultationId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var service = new Mock<IConsultationCompletionService>(MockBehavior.Strict);
        service.Setup(item => item.CompleteAsync(consultationId, doctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompleteConsultationResult(outcome));

        var result = await CreateCompletionController(service, "Doctor", doctorId.ToString())
            .Complete(consultationId, CancellationToken.None);

        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedMessage, Assert.IsType<MessageResponse>(response.Value).Message);
        service.VerifyAll();
    }

    [Theory]
    [InlineData("Receptionist", "invalid", 403)]
    [InlineData("Doctor", "invalid", 401)]
    public async Task CompleteRejectsUntrustedIdentityBeforeCallingService(
        string role, string userId, int expectedStatus)
    {
        var service = new Mock<IConsultationCompletionService>(MockBehavior.Strict);

        var result = await CreateCompletionController(service, role, userId)
            .Complete(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProgressLookupReturnsSavedVitalSignsStateForDoctor()
    {
        var queueId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var progress = new ConsultationProgressResponse(Guid.NewGuid(), queueId, "COMPLETE", true);
        var service = new Mock<IConsultationCompletionService>(MockBehavior.Strict);
        service.Setup(item => item.FindByQueueAsync(queueId, doctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(progress);

        var result = await CreateProgressController(service, "Doctor", doctorId.ToString())
            .GetForQueue(queueId, CancellationToken.None);

        Assert.Same(progress, Assert.IsType<OkObjectResult>(result).Value);
        service.VerifyAll();
    }

    [Fact]
    public async Task ProgressLookupReturnsNoContentWhenDoctorHasNoConsultation()
    {
        var queueId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var service = new Mock<IConsultationCompletionService>(MockBehavior.Strict);
        service.Setup(item => item.FindByQueueAsync(queueId, doctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsultationProgressResponse?)null);

        var result = await CreateProgressController(service, "Doctor", doctorId.ToString())
            .GetForQueue(queueId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        service.VerifyAll();
    }

    [Theory]
    [InlineData("Receptionist", "invalid", 403)]
    [InlineData("Doctor", "invalid", 401)]
    public async Task ProgressLookupRejectsUntrustedIdentityBeforeCallingService(
        string role, string userId, int expectedStatus)
    {
        var service = new Mock<IConsultationCompletionService>(MockBehavior.Strict);

        var result = await CreateProgressController(service, role, userId)
            .GetForQueue(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LatestCompletedLookupReturnsDoctorConsultationContext()
    {
        var doctorId = Guid.NewGuid();
        var context = new CompletedConsultationContextResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        var service = new Mock<IConsultationCompletionService>(MockBehavior.Strict);
        service.Setup(item => item.FindLatestCompletedAsync(
                doctorId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        var result = await CreateCompletedContextController(
                service,
                "Doctor",
                doctorId.ToString())
            .GetLatest(CancellationToken.None);

        Assert.Same(context, Assert.IsType<OkObjectResult>(result).Value);
        service.VerifyAll();
    }

    [Fact]
    public async Task LatestCompletedLookupReturnsNoContentWhenDoctorHasNoCompletedConsultation()
    {
        var doctorId = Guid.NewGuid();
        var service = new Mock<IConsultationCompletionService>(MockBehavior.Strict);
        service.Setup(item => item.FindLatestCompletedAsync(
                doctorId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CompletedConsultationContextResponse?)null);

        var result = await CreateCompletedContextController(
                service,
                "Doctor",
                doctorId.ToString())
            .GetLatest(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        service.VerifyAll();
    }

    [Theory]
    [InlineData("Receptionist", "invalid", 403)]
    [InlineData("Doctor", "invalid", 401)]
    public async Task LatestCompletedLookupRejectsUntrustedIdentityBeforeCallingService(
        string role,
        string userId,
        int expectedStatus)
    {
        var service = new Mock<IConsultationCompletionService>(MockBehavior.Strict);

        var result = await CreateCompletedContextController(service, role, userId)
            .GetLatest(CancellationToken.None);

        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        service.VerifyNoOtherCalls();
    }

    private static ConsultationCompletionController CreateCompletionController(
        Mock<IConsultationCompletionService> service, string role, string userId)
    {
        var controller = new ConsultationCompletionController(service.Object);
        SetIdentity(controller, role, userId);
        return controller;
    }

    private static ConsultationProgressController CreateProgressController(
        Mock<IConsultationCompletionService> service, string role, string userId)
    {
        var controller = new ConsultationProgressController(service.Object);
        SetIdentity(controller, role, userId);
        return controller;
    }

    private static CompletedConsultationContextController CreateCompletedContextController(
        Mock<IConsultationCompletionService> service,
        string role,
        string userId)
    {
        var controller = new CompletedConsultationContextController(service.Object);
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
