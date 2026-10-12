using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PatientService.Controllers;
using PatientService.Models.Dtos;
using PatientService.Models.Enums;
using PatientService.Services;

namespace PatientService.UnitTests.Controllers;

// Missing headers, not-found results and the check-in publish failure of the PatientService
// controllers, tested against mocked services (SWC-151 mutation testing).
public class PatientControllerEdgeCaseTests
{
    [Fact]
    public async Task AllergiesWithoutARoleHeaderAreForbidden()
    {
        var controller = With(
            new AllergiesController(Mock.Of<IAllergyService>(MockBehavior.Strict), NullLogger<AllergiesController>.Instance),
            Context(role: null));

        var result = await controller.GetAllergies(Guid.NewGuid(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
    }

    [Fact]
    public async Task AllergyAddedWithoutAUserIdIsRecordedAgainstNoUser()
    {
        Guid? actingUserId = null;
        var service = new Mock<IAllergyService>();
        service.Setup(candidate => candidate.AddAllergyAsync(
                It.IsAny<Guid>(),
                It.IsAny<AllergyRequest>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, AllergyRequest, Guid, CancellationToken>((_, _, userId, _) => actingUserId = userId)
            .ReturnsAsync((AllergyResponse?)null);
        var controller = With(
            new AllergiesController(service.Object, NullLogger<AllergiesController>.Instance),
            Context(role: "Receptionist"));

        await controller.AddAllergy(Guid.NewGuid(), new AllergyRequest(), CancellationToken.None);

        Assert.Equal(Guid.Empty, actingUserId);
    }

    [Fact]
    public async Task ChronicConditionsWithoutARoleHeaderAreForbidden()
    {
        var controller = With(
            new ChronicConditionsController(
                Mock.Of<IChronicConditionService>(MockBehavior.Strict),
                NullLogger<ChronicConditionsController>.Instance),
            Context(role: null));

        var result = await controller.GetConditions(Guid.NewGuid(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
    }

    [Fact]
    public async Task ChronicConditionsForUnknownPatientAreNotFound()
    {
        var service = new Mock<IChronicConditionService>();
        service.Setup(candidate => candidate.GetConditionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ChronicConditionResponse>?)null);
        var controller = With(
            new ChronicConditionsController(service.Object, NullLogger<ChronicConditionsController>.Instance),
            Context(role: "Doctor"));

        var result = await controller.GetConditions(Guid.NewGuid(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status404NotFound, "Patient not found");
    }

    [Fact]
    public async Task ConditionAddedForUnknownPatientIsNotFoundAndRecordedAgainstNoUser()
    {
        Guid? actingUserId = null;
        var service = new Mock<IChronicConditionService>();
        service.Setup(candidate => candidate.AddConditionAsync(
                It.IsAny<Guid>(),
                It.IsAny<ChronicConditionRequest>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, ChronicConditionRequest, Guid, CancellationToken>((_, _, userId, _) => actingUserId = userId)
            .ReturnsAsync((ChronicConditionResponse?)null);
        var controller = With(
            new ChronicConditionsController(service.Object, NullLogger<ChronicConditionsController>.Instance),
            Context(role: "Receptionist"));

        var result = await controller.AddCondition(Guid.NewGuid(), new ChronicConditionRequest(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status404NotFound, "Patient not found");
        Assert.Equal(Guid.Empty, actingUserId);
    }

    [Fact]
    public async Task RegistrationForwardsTheCallersCorrelationId()
    {
        string? forwarded = null;
        var mocks = new PatientMocks();
        mocks.Registration.Setup(candidate => candidate.RegisterPatientAsync(
                It.IsAny<RegisterPatientRequest>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Callback<RegisterPatientRequest, string, Guid, CancellationToken>((_, correlationId, _, _) => forwarded = correlationId)
            .ReturnsAsync(new RegisterPatientResult { Outcome = RegisterPatientOutcome.Success });
        var context = Context(role: "Receptionist");
        context.HttpContext.Request.Headers["X-Correlation-ID"] = "corr-123";

        await With(mocks.Controller(), context).RegisterPatient(new RegisterPatientRequest(), CancellationToken.None);

        Assert.Equal("corr-123", forwarded);
    }

    [Fact]
    public async Task UnknownPatientProfileIsNotFound()
    {
        var mocks = new PatientMocks();
        mocks.Profile.Setup(candidate => candidate.GetPatientAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PatientProfileResponse?)null);

        var result = await With(mocks.Controller(), Context(role: "Doctor")).GetPatient(Guid.NewGuid(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status404NotFound, "Patient not found");
    }

    [Fact]
    public async Task UpdatingAnUnknownPatientIsNotFound()
    {
        var mocks = new PatientMocks();
        mocks.Profile.Setup(candidate => candidate.UpdatePatientAsync(
                It.IsAny<Guid>(),
                It.IsAny<UpdatePatientRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PatientProfileResponse?)null);

        var result = await With(mocks.Controller(), Context(role: "Receptionist"))
            .UpdatePatient(Guid.NewGuid(), new UpdatePatientRequest(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status404NotFound, "Patient not found");
    }

    [Fact]
    public async Task CheckInThatCannotBePublishedAsksTheReceptionistToRetry()
    {
        var mocks = new PatientMocks();
        mocks.CheckIn.Setup(candidate => candidate.CheckInPatientAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckInPatientOutcome.EventPublishFailed);

        var result = await With(mocks.Controller(), Context(role: "Receptionist")).CheckInPatient(Guid.NewGuid(), CancellationToken.None);

        AssertMessage(result, StatusCodes.Status503ServiceUnavailable, "Unable to check in patient. Please try again.");
    }

    private static TController With<TController>(TController controller, ControllerContext context)
        where TController : ControllerBase
    {
        controller.ControllerContext = context;
        return controller;
    }

    private static ControllerContext Context(string? role)
    {
        var context = new DefaultHttpContext();
        if (role is not null)
        {
            context.Request.Headers["X-User-Role"] = role;
        }

        return new ControllerContext { HttpContext = context };
    }

    private static void AssertMessage(IActionResult result, int statusCode, string message)
    {
        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal(message, Assert.IsType<MessageResponse>(response.Value).Message);
    }

    private sealed class PatientMocks
    {
        public Mock<IPatientRegistrationService> Registration { get; } = new();

        public Mock<IPatientCheckInService> CheckIn { get; } = new();

        public Mock<IPatientSearchService> Search { get; } = new();

        public Mock<IPatientProfileService> Profile { get; } = new();

        public PatientsController Controller() => new(
            Registration.Object,
            CheckIn.Object,
            Search.Object,
            Profile.Object,
            NullLogger<PatientsController>.Instance);
    }
}
