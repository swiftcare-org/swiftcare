using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MedicalRecordService.Controllers;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Services;

namespace MedicalRecordService.UnitTests.Controllers;

// Every Doctor-only endpoint checks the trusted role header and then the doctor ID header
// before calling its service. Strict mocks prove the service is never reached when a check
// fails (SWC-151 mutation testing).
public class DoctorEndpointGuardTests
{
    private const string ValidDoctorId = "11111111-1111-1111-1111-111111111111";

    private static readonly Dictionary<string, Func<HttpContext, Task<IActionResult>>> Endpoints = new()
    {
        ["CompletedConsultationContext.GetLatest"] = context =>
            With(new CompletedConsultationContextController(Strict<IConsultationCompletionService>()), context)
                .GetLatest(CancellationToken.None),
        ["ConsultationCompletion.Complete"] = context =>
            With(new ConsultationCompletionController(Strict<IConsultationCompletionService>()), context)
                .Complete(Guid.NewGuid(), CancellationToken.None),
        ["ConsultationFollowUp.GetLatestOverdue"] = context =>
            With(new ConsultationFollowUpController(Strict<IConsultationFollowUpService>()), context)
                .GetLatestOverdue(Guid.NewGuid(), CancellationToken.None),
        ["ConsultationHistory.GetHistory"] = context =>
            With(new ConsultationHistoryController(Strict<IConsultationHistoryService>()), context)
                .GetHistory(Guid.NewGuid(), CancellationToken.None),
        ["ConsultationHistory.GetLatest"] = context =>
            With(new ConsultationHistoryController(Strict<IConsultationHistoryService>()), context)
                .GetLatest(Guid.NewGuid(), CancellationToken.None),
        ["ConsultationProgress.GetForQueue"] = context =>
            With(new ConsultationProgressController(Strict<IConsultationCompletionService>()), context)
                .GetForQueue(Guid.NewGuid(), CancellationToken.None),
        ["ConsultationVitalSigns.RecordVitalSigns"] = context =>
            With(new ConsultationVitalSignsController(Strict<IVitalSignsService>()), context)
                .RecordVitalSigns(Guid.NewGuid(), new RecordVitalSignsRequest(), CancellationToken.None),
        ["Consultations.CreateConsultation"] = context =>
            With(new ConsultationsController(Strict<IConsultationService>()), context)
                .CreateConsultation(new CreateConsultationRequest(), CancellationToken.None),
        ["VitalSignsHistory.GetHistory"] = context =>
            With(new VitalSignsHistoryController(Strict<IVitalSignsHistoryService>()), context)
                .GetHistory(Guid.NewGuid(), CancellationToken.None)
    };

    public static TheoryData<string> DoctorEndpoints => new(Endpoints.Keys);

    [Theory]
    [MemberData(nameof(DoctorEndpoints))]
    public async Task MissingRoleHeaderIsForbidden(string endpoint)
    {
        var result = await Endpoints[endpoint](Context(role: null, userId: ValidDoctorId));

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
    }

    [Theory]
    [MemberData(nameof(DoctorEndpoints))]
    public async Task NonDoctorRoleIsForbidden(string endpoint)
    {
        var result = await Endpoints[endpoint](Context(role: "Receptionist", userId: ValidDoctorId));

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
    }

    [Theory]
    [MemberData(nameof(DoctorEndpoints))]
    public async Task MissingDoctorIdIsUnauthorized(string endpoint)
    {
        var result = await Endpoints[endpoint](Context(role: "Doctor", userId: null));

        AssertMessage(result, StatusCodes.Status401Unauthorized, "Doctor identity is unavailable");
    }

    [Theory]
    [MemberData(nameof(DoctorEndpoints))]
    public async Task MalformedDoctorIdIsUnauthorized(string endpoint)
    {
        var result = await Endpoints[endpoint](Context(role: "Doctor", userId: "not-a-guid"));

        AssertMessage(result, StatusCodes.Status401Unauthorized, "Doctor identity is unavailable");
    }

    [Theory]
    [MemberData(nameof(DoctorEndpoints))]
    public async Task EmptyDoctorIdIsUnauthorized(string endpoint)
    {
        var result = await Endpoints[endpoint](Context(role: "Doctor", userId: Guid.Empty.ToString()));

        AssertMessage(result, StatusCodes.Status401Unauthorized, "Doctor identity is unavailable");
    }

    [Theory]
    [InlineData("   ", "R-204")]
    [InlineData(null, "R-204")]
    [InlineData("Dr. Amara Chen", "   ")]
    [InlineData("Dr. Amara Chen", null)]
    public async Task CreateConsultationWithoutDoctorNameOrRoomIsUnauthorized(string? doctorName, string? roomNumber)
    {
        var context = Context(role: "Doctor", userId: ValidDoctorId, doctorName, roomNumber);

        var result = await Endpoints["Consultations.CreateConsultation"](context);

        AssertMessage(result, StatusCodes.Status401Unauthorized, "Doctor identity is unavailable");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Receptionist")]
    public async Task TemplatesAreForbiddenToAnyoneButDoctors(string? role)
    {
        var controller = With(new TemplatesController(Strict<IConsultationTemplateService>()), Context(role, ValidDoctorId));

        var result = await controller.GetTemplates(CancellationToken.None);

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
    }

    private static T Strict<T>() where T : class => new Mock<T>(MockBehavior.Strict).Object;

    private static TController With<TController>(TController controller, HttpContext context)
        where TController : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private static HttpContext Context(
        string? role,
        string? userId,
        string? doctorName = "Dr. Amara Chen",
        string? roomNumber = "R-204")
    {
        var context = new DefaultHttpContext();
        SetHeader(context, "X-User-Role", role);
        SetHeader(context, "X-User-Id", userId);
        SetHeader(context, "X-User-Name", doctorName);
        SetHeader(context, "X-Room-Number", roomNumber);
        return context;
    }

    private static void SetHeader(HttpContext context, string name, string? value)
    {
        if (value is not null)
        {
            context.Request.Headers[name] = value;
        }
    }

    private static void AssertMessage(IActionResult result, int statusCode, string message)
    {
        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal(message, Assert.IsType<MessageResponse>(response.Value).Message);
    }
}
