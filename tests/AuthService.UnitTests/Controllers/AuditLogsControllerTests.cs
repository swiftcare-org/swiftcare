using System.Net;
using System.Net.Http.Json;
using AuthService.Models.Dtos;
using AuthService.Services;
using Moq;

namespace AuthService.UnitTests.Controllers;

// SWC-138: GET /api/audit-logs is admin-only and read-only.
public class AuditLogsControllerTests
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";
    private const string UserRoleHeaderName = "X-User-Role";

    private static HttpClient CreateClient(AuthServiceWebApplicationFactory factory, string? role = "Admin")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, AuthServiceWebApplicationFactory.ValidGatewaySecret);
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(UserRoleHeaderName, role);
        }

        return client;
    }

    private static void SetupEntries(AuthServiceWebApplicationFactory factory, params AuditLogEntryResponse[] entries) =>
        factory.AuditLogServiceMock
            .Setup(s => s.GetRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

    [Fact]
    public async Task GetAuditLogReturns200WithUsernameActionTimestampAndIpAddress()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var occurredAt = new DateTime(2026, 10, 6, 8, 30, 0, DateTimeKind.Utc);
        SetupEntries(factory, new AuditLogEntryResponse
        {
            Id = Guid.NewGuid(),
            Username = "admin",
            Action = "UserDeactivated",
            TargetUsername = "dr.chen",
            OccurredAt = occurredAt,
            IpAddress = "203.0.113.7"
        });

        var response = await CreateClient(factory).GetAsync("/api/audit-logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("2026-10-06T08:30:00Z", rawBody);
        var entry = Assert.Single((await response.Content.ReadFromJsonAsync<List<AuditLogEntryResponse>>())!);
        Assert.Equal("admin", entry.Username);
        Assert.Equal("UserDeactivated", entry.Action);
        Assert.Equal("dr.chen", entry.TargetUsername);
        Assert.Equal("203.0.113.7", entry.IpAddress);
    }

    [Fact]
    public async Task GetAuditLogWithoutALimitUsesTheDefault()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        SetupEntries(factory);

        await CreateClient(factory).GetAsync("/api/audit-logs");

        factory.AuditLogServiceMock.Verify(
            s => s.GetRecentAsync(AuditLogService.DefaultLimit, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAuditLogPassesTheRequestedLimitToTheService()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        SetupEntries(factory);

        await CreateClient(factory).GetAsync("/api/audit-logs?limit=25");

        factory.AuditLogServiceMock.Verify(s => s.GetRecentAsync(25, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAuditLogWithANonNumericLimitReturns400()
    {
        using var factory = new AuthServiceWebApplicationFactory();

        var response = await CreateClient(factory).GetAsync("/api/audit-logs?limit=all");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        factory.AuditLogServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    [InlineData(null)]
    public async Task GetAuditLogWithoutTheAdminRoleReturns403AndNeverCallsTheService(string? role)
    {
        using var factory = new AuthServiceWebApplicationFactory();

        var response = await CreateClient(factory, role).GetAsync("/api/audit-logs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.AuditLogServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAuditLogWithoutTheGatewaySecretReturns401()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Admin");

        var response = await client.GetAsync("/api/audit-logs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task TheAuditLogCannotBeWrittenThroughTheApi(string method)
    {
        using var factory = new AuthServiceWebApplicationFactory();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/audit-logs");

        var response = await CreateClient(factory).SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
