using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using NotificationService.Models.Dtos;

namespace NotificationService.UnitTests.Controllers;

// SWC-140: GET /api/reports/daily through the real HTTP pipeline.
public class ReportsControllerTests
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";
    private const string UserRoleHeaderName = "X-User-Role";

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
    public async Task AdminGetsTheReportForTheRequestedDate()
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        var date = new DateOnly(2026, 10, 8);
        factory.DailyReportServiceMock
            .Setup(service => service.GetAsync(date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyReportResponse(
                date,
                TotalPatients: 7,
                NewPatients: 2,
                ReturningPatients: 5,
                [new RoomPatientCount("1", 4), new RoomPatientCount("2", 3), new RoomPatientCount("3", 0)],
                [new DiagnosisCount("Viral URTI", 6)]));

        using var response = await CreateClient(factory, "Admin").GetAsync("/api/reports/daily?date=2026-10-08");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = body.RootElement;
        Assert.Equal("2026-10-08", report.GetProperty("date").GetString());
        Assert.Equal(7, report.GetProperty("totalPatients").GetInt32());
        Assert.Equal(2, report.GetProperty("newPatients").GetInt32());
        Assert.Equal(5, report.GetProperty("returningPatients").GetInt32());
        var rooms = report.GetProperty("patientsPerRoom").EnumerateArray().ToArray();
        Assert.Equal(3, rooms.Length);
        Assert.Equal("1", rooms[0].GetProperty("roomNumber").GetString());
        Assert.Equal(4, rooms[0].GetProperty("patients").GetInt32());
        var diagnosis = Assert.Single(report.GetProperty("topDiagnoses").EnumerateArray());
        Assert.Equal("Viral URTI", diagnosis.GetProperty("diagnosis").GetString());
        Assert.Equal(6, diagnosis.GetProperty("count").GetInt32());
        factory.DailyReportServiceMock.Verify(
            service => service.GetAsync(date, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?date=")]
    [InlineData("?date=abc")]
    [InlineData("?date=2026-13-01")]
    [InlineData("?date=2026-02-30")]
    [InlineData("?date=08-10-2026")]
    [InlineData("?date=2026-10-08T00:00:00")]
    public async Task MissingOrInvalidDateReturns400AndNeverCallsTheService(string query)
    {
        using var factory = new NotificationServiceWebApplicationFactory();

        using var response = await CreateClient(factory, "Admin").GetAsync($"/api/reports/daily{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Date is required in the format yyyy-MM-dd", body!.Message);
        factory.DailyReportServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    [InlineData("admin")]
    public async Task ReportIsForbiddenToOtherRoles(string role)
    {
        using var factory = new NotificationServiceWebApplicationFactory();

        using var response = await CreateClient(factory, role).GetAsync("/api/reports/daily?date=2026-10-08");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Forbidden", body!.Message);
        factory.DailyReportServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RequestWithoutARoleReturns401AndNeverCallsTheService(string? role)
    {
        using var factory = new NotificationServiceWebApplicationFactory();

        using var response = await CreateClient(factory, role).GetAsync("/api/reports/daily?date=2026-10-08");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("User identity is unavailable", body!.Message);
        factory.DailyReportServiceMock.VerifyNoOtherCalls();
    }

    // The role is checked before the date, so a caller without access learns nothing about the input.
    [Fact]
    public async Task RoleIsCheckedBeforeTheDate()
    {
        using var factory = new NotificationServiceWebApplicationFactory();

        using var response = await CreateClient(factory, "Doctor").GetAsync("/api/reports/daily?date=abc");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RequestThatDidNotComeThroughTheGatewayReturns401()
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Admin");

        using var response = await client.GetAsync("/api/reports/daily?date=2026-10-08");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.DailyReportServiceMock.VerifyNoOtherCalls();
    }
}
