using System.Net;
using System.Net.Http.Json;
using Moq;
using NotificationService.Models.Dtos;
using NotificationService.Services;

namespace NotificationService.UnitTests.Controllers;

public sealed class TodaysNotificationsControllerTests
{
    private static HttpClient CreateClient(NotificationServiceWebApplicationFactory factory, string? role, bool secret = true)
    {
        var client = factory.CreateClient();
        if (secret) client.DefaultRequestHeaders.Add("X-Gateway-Secret", NotificationServiceWebApplicationFactory.ValidGatewaySecret);
        if (role is not null) client.DefaultRequestHeaders.Add("X-User-Role", role);
        return client;
    }

    [Theory]
    [InlineData("Receptionist", "", NotificationFeedService.DefaultLimit)]
    [InlineData("Admin", "?limit=10", 10)]
    public async Task AuthorizedRequestsReturnOnlyTheTodayServiceResult(string role, string query, int limit)
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        var entry = new NotificationResponse(Guid.NewGuid(), "PatientCheckedIn", Guid.NewGuid(), DateTime.UtcNow, IsNewPatient: true);
        factory.FeedServiceMock.Setup(service => service.GetTodayAsync(limit, It.IsAny<CancellationToken>())).ReturnsAsync([entry]);
        using var client = CreateClient(factory, role);

        using var response = await client.GetAsync("/api/notifications/today" + query);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(entry, Assert.Single((await response.Content.ReadFromJsonAsync<List<NotificationResponse>>())!));
        factory.FeedServiceMock.Verify(service => service.GetTodayAsync(limit, It.IsAny<CancellationToken>()), Times.Once);
        factory.FeedServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NoActivityReturnsAnEmptyList()
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        factory.FeedServiceMock.Setup(service => service.GetTodayAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        using var client = CreateClient(factory, "Receptionist");
        using var response = await client.GetAsync("/api/notifications/today");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<NotificationResponse>>())!);
    }

    [Theory]
    [InlineData(null, true, HttpStatusCode.Unauthorized)]
    [InlineData("", true, HttpStatusCode.Unauthorized)]
    [InlineData("Doctor", true, HttpStatusCode.Forbidden)]
    [InlineData("receptionist", true, HttpStatusCode.Forbidden)]
    [InlineData("Receptionist", false, HttpStatusCode.Unauthorized)]
    public async Task UntrustedOrUnauthorizedRequestsNeverReadTheFeed(string? role, bool secret, HttpStatusCode expected)
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        using var client = CreateClient(factory, role, secret);
        using var response = await client.GetAsync("/api/notifications/today");
        Assert.Equal(expected, response.StatusCode);
        factory.FeedServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvalidLimitDoesNotReadTheFeed()
    {
        using var factory = new NotificationServiceWebApplicationFactory();
        using var client = CreateClient(factory, "Admin");
        using var response = await client.GetAsync("/api/notifications/today?limit=all");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        factory.FeedServiceMock.VerifyNoOtherCalls();
    }
}
