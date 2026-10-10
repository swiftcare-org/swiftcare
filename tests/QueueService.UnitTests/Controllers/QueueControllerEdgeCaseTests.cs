using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using QueueService.Controllers;
using QueueService.Models.Dtos;
using QueueService.Models.Enums;
using QueueService.Services;

namespace QueueService.UnitTests.Controllers;

// Role checks, identity headers, correlation IDs and outcome mappings of QueueController,
// tested directly against mocked services (SWC-151 mutation testing).
public class QueueControllerEdgeCaseTests
{
    private const string DoctorId = "11111111-1111-1111-1111-111111111111";

    public static TheoryData<string, string?> ForbiddenRequests => new()
    {
        { nameof(QueueController.GetToday), null },
        { nameof(QueueController.GetToday), "Doctor" },
        { nameof(QueueController.GetWaiting), null },
        { nameof(QueueController.GetWaiting), "Receptionist" },
        { nameof(QueueController.GetCurrent), null },
        { nameof(QueueController.GetCurrent), "Receptionist" },
        { nameof(QueueController.CallNext), null },
        { nameof(QueueController.CallNext), "Admin" },
        { nameof(QueueController.GetTodayPatientStatus), null },
        { nameof(QueueController.GetTodayPatientStatus), "Doctor" }
    };

    [Theory]
    [MemberData(nameof(ForbiddenRequests))]
    public async Task EndpointsRejectMissingOrWrongRoleWithForbiddenMessage(string action, string? role)
    {
        var mocks = new ServiceMocks();
        var controller = CreateController(mocks, role, DoctorId, "Dr. Amara Chen", "R-204");

        var result = action switch
        {
            nameof(QueueController.GetToday) => await controller.GetToday(CancellationToken.None),
            nameof(QueueController.GetWaiting) => await controller.GetWaiting(CancellationToken.None),
            nameof(QueueController.GetCurrent) => await controller.GetCurrent(CancellationToken.None),
            nameof(QueueController.CallNext) => await controller.CallNext(CancellationToken.None),
            _ => await controller.GetTodayPatientStatus(Guid.NewGuid(), CancellationToken.None)
        };

        AssertMessage(result, StatusCodes.Status403Forbidden, "Forbidden");
        mocks.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetCurrentWithoutValidDoctorIdIsUnauthorized(string? userId)
    {
        var mocks = new ServiceMocks();

        var result = await CreateController(mocks, "Doctor", userId).GetCurrent(CancellationToken.None);

        AssertMessage(result, StatusCodes.Status401Unauthorized, "Doctor identity is unavailable");
        mocks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetCurrentReturnsNoContentResultWhenDoctorHasNoActivePatient()
    {
        var mocks = new ServiceMocks();
        mocks.TodayQueue
            .Setup(service => service.GetCurrentForDoctorAsync(Guid.Parse(DoctorId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalledPatientResponse?)null);

        var result = await CreateController(mocks, "Doctor", DoctorId).GetCurrent(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Theory]
    [InlineData("not-a-guid", "Dr. Amara Chen", "R-204")]
    [InlineData("00000000-0000-0000-0000-000000000000", "Dr. Amara Chen", "R-204")]
    [InlineData(DoctorId, "   ", "R-204")]
    [InlineData(DoctorId, null, "R-204")]
    [InlineData(DoctorId, "Dr. Amara Chen", "   ")]
    [InlineData(DoctorId, "Dr. Amara Chen", null)]
    public async Task CallNextWithIncompleteDoctorIdentityIsUnauthorized(
        string userId,
        string? doctorName,
        string? roomNumber)
    {
        var mocks = new ServiceMocks();

        var result = await CreateController(mocks, "Doctor", userId, doctorName, roomNumber)
            .CallNext(CancellationToken.None);

        AssertMessage(result, StatusCodes.Status401Unauthorized, "Doctor identity is unavailable");
        mocks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CallNextForwardsTheCallersCorrelationId()
    {
        var mocks = new ServiceMocks();
        var correlationId = CaptureCorrelationId(mocks, CallNextPatientOutcome.NoPatientsWaiting);

        await CreateController(mocks, "Doctor", DoctorId, "Dr. Amara Chen", "R-204", "corr-123")
            .CallNext(CancellationToken.None);

        Assert.Equal("corr-123", correlationId.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task CallNextGeneratesACorrelationIdWhenTheCallerSentNone(string? header)
    {
        var mocks = new ServiceMocks();
        var correlationId = CaptureCorrelationId(mocks, CallNextPatientOutcome.NoPatientsWaiting);

        await CreateController(mocks, "Doctor", DoctorId, "Dr. Amara Chen", "R-204", header)
            .CallNext(CancellationToken.None);

        Assert.True(Guid.TryParse(correlationId.Value, out _));
    }

    [Fact]
    public async Task CallNextWhenEventCannotBePublishedReturnsServiceUnavailable()
    {
        var mocks = new ServiceMocks();
        CaptureCorrelationId(mocks, CallNextPatientOutcome.EventPublishFailed);

        var result = await CreateController(mocks, "Doctor", DoctorId, "Dr. Amara Chen", "R-204")
            .CallNext(CancellationToken.None);

        AssertMessage(
            result,
            StatusCodes.Status503ServiceUnavailable,
            "Unable to call next patient. Please try again.");
    }

    // SWC-128: other doctors kept calling at the same moment; nothing changed and the doctor can retry.
    [Fact]
    public async Task CallNextThatKeptCollidingReturnsServiceUnavailable()
    {
        var mocks = new ServiceMocks();
        CaptureCorrelationId(mocks, CallNextPatientOutcome.ConcurrentCallConflict);

        var result = await CreateController(mocks, "Doctor", DoctorId, "Dr. Amara Chen", "R-204")
            .CallNext(CancellationToken.None);

        AssertMessage(
            result,
            StatusCodes.Status503ServiceUnavailable,
            "Another doctor is calling a patient at the same moment. Please try again.");
    }

    [Fact]
    public async Task CallNextSuccessWithoutPatientIsAnInvariantViolation()
    {
        var mocks = new ServiceMocks();
        CaptureCorrelationId(mocks, CallNextPatientOutcome.Success);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateController(mocks, "Doctor", DoctorId, "Dr. Amara Chen", "R-204").CallNext(CancellationToken.None));

        Assert.Equal("A successful call-next result must include the called patient.", exception.Message);
    }

    [Fact]
    public async Task CallNextWithUnknownOutcomeIsRejected()
    {
        var mocks = new ServiceMocks();
        CaptureCorrelationId(mocks, (CallNextPatientOutcome)99);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CreateController(mocks, "Doctor", DoctorId, "Dr. Amara Chen", "R-204").CallNext(CancellationToken.None));

        Assert.StartsWith("Unsupported call-next outcome.", exception.Message);
    }

    private static Box CaptureCorrelationId(ServiceMocks mocks, CallNextPatientOutcome outcome)
    {
        var captured = new Box();
        mocks.CallNext
            .Setup(service => service.CallNextAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, string, CancellationToken>((_, _, _, correlationId, _) =>
                captured.Value = correlationId)
            .ReturnsAsync(new CallNextPatientResult { Outcome = outcome });
        return captured;
    }

    private static void AssertMessage(IActionResult result, int statusCode, string message)
    {
        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal(message, Assert.IsType<MessageResponse>(response.Value).Message);
    }

    private static QueueController CreateController(
        ServiceMocks mocks,
        string? role,
        string? userId = null,
        string? doctorName = null,
        string? roomNumber = null,
        string? correlationId = null)
    {
        var context = new DefaultHttpContext();
        SetHeader(context, "X-User-Role", role);
        SetHeader(context, "X-User-Id", userId);
        SetHeader(context, "X-User-Name", doctorName);
        SetHeader(context, "X-Room-Number", roomNumber);
        SetHeader(context, "X-Correlation-ID", correlationId);

        return new QueueController(mocks.PatientStatus.Object, mocks.TodayQueue.Object, mocks.CallNext.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static void SetHeader(HttpContext context, string name, string? value)
    {
        if (value is not null)
        {
            context.Request.Headers[name] = value;
        }
    }

    private sealed class Box
    {
        public string? Value { get; set; }
    }

    private sealed class ServiceMocks
    {
        public Mock<IPatientQueueStatusService> PatientStatus { get; } = new();

        public Mock<ITodayQueueService> TodayQueue { get; } = new();

        public Mock<ICallNextPatientService> CallNext { get; } = new();

        public void VerifyNoOtherCalls()
        {
            PatientStatus.VerifyNoOtherCalls();
            TodayQueue.VerifyNoOtherCalls();
            CallNext.VerifyNoOtherCalls();
        }
    }
}
