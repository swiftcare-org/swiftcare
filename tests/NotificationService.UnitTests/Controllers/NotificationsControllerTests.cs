using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using NotificationService.Models.Dtos;
using NotificationService.Services;

namespace NotificationService.UnitTests.Controllers;

// SWC-143: GET /api/notifications through the real HTTP pipeline.
public class NotificationsControllerTests
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

    private static void SetupFeed(
        NotificationServiceWebApplicationFactory factory,
        params NotificationResponse[] notifications) =>
        factory.FeedServiceMock
            .Setup(service => service.GetRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notifications);

    [Theory]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task FeedIsReturnedToReceptionAndAdmin(string role)
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        SetupFeed(factory, new NotificationResponse(
            Guid.NewGuid(),
            "PatientCalled",
            patientId,
            new DateTime(2026, 10, 8, 4, 30, 0, DateTimeKind.Utc),
            QueueNumber: "Q-007",
            DoctorName: "Dr. Silva",
            RoomNumber: "1"));

        using var response = await CreateClient(factory, role).GetAsync("/api/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entry = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal("PatientCalled", entry.GetProperty("type").GetString());
        Assert.Equal(patientId, entry.GetProperty("patientId").GetGuid());
        Assert.Equal("2026-10-08T04:30:00Z", entry.GetProperty("occurredAt").GetString());
        Assert.Equal("Q-007", entry.GetProperty("queueNumber").GetString());
        Assert.Equal("Dr. Silva", entry.GetProperty("doctorName").GetString());
        Assert.Equal("1", entry.GetProperty("roomNumber").GetString());
    }

    [Fact]
    public async Task EmptyFeedReturnsAnEmptyList()
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        SetupFeed(factory);

        using var response = await CreateClient(factory, "Receptionist").GetAsync("/api/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<NotificationResponse>>())!);
    }

    [Fact]
    public async Task FeedWithoutALimitUsesTheDefault()
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        SetupFeed(factory);

        using var response = await CreateClient(factory, "Receptionist").GetAsync("/api/notifications");

        factory.FeedServiceMock.Verify(
            service => service.GetRecentAsync(NotificationFeedService.DefaultLimit, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestedLimitIsPassedToTheService()
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        SetupFeed(factory);

        using var response = await CreateClient(factory, "Receptionist").GetAsync("/api/notifications?limit=10");

        factory.FeedServiceMock.Verify(
            service => service.GetRecentAsync(10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NonNumericLimitReturns400AndNeverCallsTheService()
    {
        using var factory = new NotificationServiceWebApplicationFactory();

        using var response = await CreateClient(factory, "Receptionist").GetAsync("/api/notifications?limit=all");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        factory.FeedServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("receptionist")]
    public async Task FeedIsForbiddenToOtherRoles(string role)
    {
        using var factory = new NotificationServiceWebApplicationFactory();

        using var response = await CreateClient(factory, role).GetAsync("/api/notifications");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Forbidden", body!.Message);
        factory.FeedServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RequestWithoutARoleReturns401AndNeverCallsTheService(string? role)
    {
        using var factory = new NotificationServiceWebApplicationFactory();

        using var response = await CreateClient(factory, role).GetAsync("/api/notifications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("User identity is unavailable", body!.Message);
        factory.FeedServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RequestThatDidNotComeThroughTheGatewayReturns401()
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Receptionist");

        using var response = await client.GetAsync("/api/notifications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.FeedServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HealthCheckNeedsNoGatewaySecret()
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The feed is read-only: nothing can add, change or remove an entry through the API.
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task FeedCannotBeWrittenThroughTheApi(string method)
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/notifications");

        using var response = await CreateClient(factory, "Admin").SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
