using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PrescriptionService.Controllers;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Services;

namespace PrescriptionService.UnitTests.Controllers;

// SWC-142: the daily prescription report.
public class PrescriptionReportsControllerTests
{
    [Fact]
    public async Task DailyReportAsAdminReturnsTotalsForTheRequestedDate()
    {
        var date = new DateOnly(2026, 10, 2);
        var report = new PrescriptionDailyReportResponse(date, 5, 3, 2);
        var service = new Mock<IPrescriptionReportService>(MockBehavior.Strict);
        service.Setup(item => item.GetDailyReportAsync(date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var result = await CreateReportController(service, "Admin")
            .GetDailyReport(date, CancellationToken.None);

        Assert.Same(report, Assert.IsType<OkObjectResult>(result).Value);
        service.VerifyAll();
    }

    [Fact]
    public async Task DailyReportWithoutADatePassesNullSoTheServiceUsesToday()
    {
        var report = new PrescriptionDailyReportResponse(new DateOnly(2026, 10, 2), 0, 0, 0);
        var service = new Mock<IPrescriptionReportService>(MockBehavior.Strict);
        service.Setup(item => item.GetDailyReportAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var result = await CreateReportController(service, "Admin")
            .GetDailyReport(date: null, CancellationToken.None);

        Assert.Same(report, Assert.IsType<OkObjectResult>(result).Value);
        service.VerifyAll();
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    public async Task DailyReportAsNonAdminReturnsForbidden(string role)
    {
        var service = new Mock<IPrescriptionReportService>(MockBehavior.Strict);

        var result = await CreateReportController(service, role)
            .GetDailyReport(new DateOnly(2026, 10, 2), CancellationToken.None);

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            Assert.IsType<ObjectResult>(result).StatusCode);
        service.VerifyNoOtherCalls();
    }

    private static PrescriptionReportsController CreateReportController(
        Mock<IPrescriptionReportService> service,
        string role) => WithRole(new PrescriptionReportsController(service.Object), role);

    private static TController WithRole<TController>(TController controller, string role)
        where TController : ControllerBase
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Role"] = role;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }
}
