using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MedicalRecordService.Controllers;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Enums;
using MedicalRecordService.Services;

namespace MedicalRecordService.UnitTests.Controllers;

// How each Doctor endpoint turns a service outcome into an HTTP response, including the
// invariant violations that must never be returned as success (SWC-151 mutation testing).
public class ControllerOutcomeMappingTests
{
    private static readonly Guid DoctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task FollowUpReturnsNoContentWhenNothingIsOverdue()
    {
        var service = new Mock<IConsultationFollowUpService>();
        service.Setup(candidate => candidate.FindOverdueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OverdueFollowUpResponse?)null);

        var result = await AsDoctor(new ConsultationFollowUpController(service.Object))
            .GetLatestOverdue(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task FollowUpReturnsTheOverdueFollowUpForThePatient()
    {
        var patientId = Guid.NewGuid();
        var overdue = new OverdueFollowUpResponse(Guid.NewGuid(), new DateOnly(2026, 9, 1), "Review blood pressure", "Dr. Amara Chen", 21);
        var service = new Mock<IConsultationFollowUpService>();
        service.Setup(candidate => candidate.FindOverdueAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overdue);

        var result = await AsDoctor(new ConsultationFollowUpController(service.Object))
            .GetLatestOverdue(patientId, CancellationToken.None);

        Assert.Same(overdue, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task RecordingVitalSignsReturnsCreatedWithTheRecordedValues()
    {
        var consultationId = Guid.NewGuid();
        var recorded = new VitalSignsResponse
        {
            Id = Guid.NewGuid(),
            ConsultationId = consultationId,
            PulseRate = 72,
            RecordedAt = new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc)
        };
        var service = VitalSignsServiceReturning(new RecordVitalSignsResult
        {
            Outcome = RecordVitalSignsOutcome.Success,
            VitalSigns = recorded
        });

        var result = await AsDoctor(new ConsultationVitalSignsController(service.Object))
            .RecordVitalSigns(consultationId, new RecordVitalSignsRequest(), CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.Same(recorded, response.Value);
    }

    [Theory]
    [InlineData(RecordVitalSignsOutcome.ConsultationNotFound, StatusCodes.Status404NotFound, "Consultation was not found for this doctor")]
    [InlineData(RecordVitalSignsOutcome.VitalSignsAlreadyExist, StatusCodes.Status409Conflict, "Vital signs already exist for this consultation")]
    public async Task RecordingVitalSignsMapsEachFailure(RecordVitalSignsOutcome outcome, int statusCode, string message)
    {
        var service = VitalSignsServiceReturning(new RecordVitalSignsResult { Outcome = outcome });

        var result = await AsDoctor(new ConsultationVitalSignsController(service.Object))
            .RecordVitalSigns(Guid.NewGuid(), new RecordVitalSignsRequest(), CancellationToken.None);

        AssertMessage(result, statusCode, message);
    }

    [Fact]
    public async Task RecordingVitalSignsSuccessWithoutValuesIsAnInvariantViolation()
    {
        var service = VitalSignsServiceReturning(new RecordVitalSignsResult { Outcome = RecordVitalSignsOutcome.Success });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AsDoctor(new ConsultationVitalSignsController(service.Object))
                .RecordVitalSigns(Guid.NewGuid(), new RecordVitalSignsRequest(), CancellationToken.None));

        Assert.Equal("A successful vital-signs result must include the recorded values.", exception.Message);
    }

    [Fact]
    public async Task RecordingVitalSignsWithUnknownOutcomeIsRejected()
    {
        var service = VitalSignsServiceReturning(new RecordVitalSignsResult { Outcome = (RecordVitalSignsOutcome)99 });

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            AsDoctor(new ConsultationVitalSignsController(service.Object))
                .RecordVitalSigns(Guid.NewGuid(), new RecordVitalSignsRequest(), CancellationToken.None));

        Assert.StartsWith("Unsupported record-vital-signs outcome.", exception.Message);
    }

    [Fact]
    public async Task CreatingASecondConsultationForTheQueueReturnsConflict()
    {
        var service = ConsultationServiceReturning(new CreateConsultationResult
        {
            Outcome = CreateConsultationOutcome.QueueAlreadyHasConsultation
        });

        var result = await AsDoctor(new ConsultationsController(service.Object))
            .CreateConsultation(new CreateConsultationRequest(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status409Conflict, "A consultation already exists for this queue entry");
    }

    [Fact]
    public async Task CreatingAConsultationWithAnUnavailableTemplateIsAValidationProblem()
    {
        var service = ConsultationServiceReturning(new CreateConsultationResult
        {
            Outcome = CreateConsultationOutcome.TemplateNotFound
        });

        var result = await AsDoctor(new ConsultationsController(service.Object))
            .CreateConsultation(new CreateConsultationRequest(), CancellationToken.None);

        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(response.Value);
        Assert.Equal(["Selected template is unavailable"], problem.Errors[nameof(CreateConsultationRequest.TemplateId)]);
    }

    [Fact]
    public async Task CreatingAConsultationSuccessWithoutConsultationIsAnInvariantViolation()
    {
        var service = ConsultationServiceReturning(new CreateConsultationResult { Outcome = CreateConsultationOutcome.Success });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AsDoctor(new ConsultationsController(service.Object))
                .CreateConsultation(new CreateConsultationRequest(), CancellationToken.None));

        Assert.Equal("A successful consultation result must include the created consultation.", exception.Message);
    }

    [Fact]
    public async Task CreatingAConsultationWithUnknownOutcomeIsRejected()
    {
        var service = ConsultationServiceReturning(new CreateConsultationResult { Outcome = (CreateConsultationOutcome)99 });

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            AsDoctor(new ConsultationsController(service.Object))
                .CreateConsultation(new CreateConsultationRequest(), CancellationToken.None));

        Assert.StartsWith("Unsupported create-consultation outcome.", exception.Message);
    }

    [Theory]
    [InlineData(CompleteConsultationOutcome.Success)]
    [InlineData((CompleteConsultationOutcome)99)]
    public async Task CompletionWithoutAnEventOrWithUnknownOutcomeIsRejected(CompleteConsultationOutcome outcome)
    {
        var service = new Mock<IConsultationCompletionService>();
        service.Setup(candidate => candidate.CompleteAsync(It.IsAny<Guid>(), DoctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompleteConsultationResult(outcome));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AsDoctor(new ConsultationCompletionController(service.Object))
                .Complete(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("Unsupported consultation completion result.", exception.Message);
    }

    private static Mock<IVitalSignsService> VitalSignsServiceReturning(RecordVitalSignsResult result)
    {
        var service = new Mock<IVitalSignsService>();
        service.Setup(candidate => candidate.RecordAsync(
                It.IsAny<Guid>(),
                DoctorId,
                It.IsAny<RecordVitalSignsRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return service;
    }

    private static Mock<IConsultationService> ConsultationServiceReturning(CreateConsultationResult result)
    {
        var service = new Mock<IConsultationService>();
        service.Setup(candidate => candidate.CreateAsync(
                It.IsAny<CreateConsultationRequest>(),
                DoctorId,
                "Dr. Amara Chen",
                "R-204",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return service;
    }

    private static TController AsDoctor<TController>(TController controller)
        where TController : ControllerBase
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Headers["X-User-Role"] = "Doctor";
        context.Request.Headers["X-User-Id"] = DoctorId.ToString();
        context.Request.Headers["X-User-Name"] = "Dr. Amara Chen";
        context.Request.Headers["X-Room-Number"] = "R-204";
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private static void AssertMessage(IActionResult result, int statusCode, string message)
    {
        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal(message, Assert.IsType<MessageResponse>(response.Value).Message);
    }
}
