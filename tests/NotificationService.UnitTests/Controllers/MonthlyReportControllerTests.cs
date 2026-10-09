using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using NotificationService.Models.Dtos;

namespace NotificationService.UnitTests.Controllers;

// SWC-145: GET /api/reports/monthly through the real HTTP pipeline.
public class MonthlyReportControllerTests
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";
    private const string UserRoleHeaderName = "X-User-Role";
    private const string Path = "/api/reports/monthly";

    private static HttpClient CreateClient(NotificationServiceWebApplicationFactory factory, string? role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            GatewaySecretHeaderName, NotificationServiceWebApplicationFactory.ValidGatewaySecret);
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(UserRoleHeaderName, role);
        }

        return client;
    }

    [Fact]
    public async Task AdminGetsTheReportForTheRequestedMonth()
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        var month = new DateOnly(2026, 10, 1);
        factory.MonthlyReportServiceMock
            .Setup(service => service.GetAsync(month, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonthlyReportResponse(
                "2026-10",
                TotalPatients: 10,
                NewPatients: 3,
                ReturningPatients: 7,
                [new DiagnosisCount("Viral URTI", 6)],
                [
                    new WeekPatientCount(1, 4),
                    new WeekPatientCount(2, 3),
                    new WeekPatientCount(3, 2),
                    new WeekPatientCount(4, 1)
                ]));

        using var response = await CreateClient(factory, "Admin").GetAsync($"{Path}?month=2026-10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = body.RootElement;
        Assert.Equal("2026-10", report.GetProperty("month").GetString());
        Assert.Equal(10, report.GetProperty("totalPatients").GetInt32());
        Assert.Equal(3, report.GetProperty("newPatients").GetInt32());
        Assert.Equal(7, report.GetProperty("returningPatients").GetInt32());
        var diagnosis = Assert.Single(report.GetProperty("topDiagnoses").EnumerateArray());
        Assert.Equal("Viral URTI", diagnosis.GetProperty("diagnosis").GetString());
        Assert.Equal(6, diagnosis.GetProperty("count").GetInt32());
        var weeks = report.GetProperty("weeklyBreakdown").EnumerateArray().ToArray();
        Assert.Equal(4, weeks.Length);
        Assert.Equal(1, weeks[0].GetProperty("week").GetInt32());
        Assert.Equal(4, weeks[0].GetProperty("patients").GetInt32());
        Assert.Equal(4, weeks[3].GetProperty("week").GetInt32());
        Assert.Equal(1, weeks[3].GetProperty("patients").GetInt32());
        factory.MonthlyReportServiceMock.Verify(
            service => service.GetAsync(month, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?month=")]
    [InlineData("?month=October")]
    [InlineData("?month=2026-13")]
    [InlineData("?month=2026-00")]
    [InlineData("?month=2026-10-08")]
    [InlineData("?month=10-2026")]
    [InlineData("?month=2026")]
    public async Task MissingOrInvalidMonthReturns400AndNeverCallsTheService(string query)
    {
        using var factory = new NotificationServiceWebApplicationFactory();

        using var response = await CreateClient(factory, "Admin").GetAsync($"{Path}{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Month is required in the format yyyy-MM", body!.Message);
        factory.MonthlyReportServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    [InlineData("admin")]
    public async Task ReportIsForbiddenToOtherRoles(string role)
    {
        using var factory = new NotificationServiceWebApplicationFactory();

        using var response = await CreateClient(factory, role).GetAsync($"{Path}?month=2026-10");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Forbidden", body!.Message);
        factory.MonthlyReportServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RequestWithoutARoleReturns401AndNeverCallsTheService(string? role)
    {
        using var factory = new NotificationServiceWebApplicationFactory();

        using var response = await CreateClient(factory, role).GetAsync($"{Path}?month=2026-10");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("User identity is unavailable", body!.Message);
        factory.MonthlyReportServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RoleIsCheckedBeforeTheMonth()
    {
        using var factory = new NotificationServiceWebApplicationFactory();

        using var response = await CreateClient(factory, "Doctor").GetAsync($"{Path}?month=October");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RequestThatDidNotComeThroughTheGatewayReturns401()
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Admin");

        using var response = await client.GetAsync($"{Path}?month=2026-10");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.MonthlyReportServiceMock.VerifyNoOtherCalls();
    }
}
