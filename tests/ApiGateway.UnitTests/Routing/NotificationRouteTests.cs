using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGateway.UnitTests.Routing;

// SWC-143, SWC-140 and SWC-145: gateway authorization for the NotificationService
// endpoints: the activity feed, the daily report and the monthly report.
public class NotificationRouteTests
{
    private const string FeedPath = "/api/notifications";
    private const string DailyReportPath = "/api/reports/daily?date=2026-10-08";
    private const string MonthlyReportPath = "/api/reports/monthly?month=2026-10";

    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? role)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        if (role is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", factory.CreateSignedToken(role: role));
        }

        using var request = new HttpRequestMessage(method, path);
        return await client.SendAsync(request);
    }

    // NotificationService is not running here, so a proxied request fails downstream. These
    // assert only that the Gateway itself matched the route and let the role through.
    [Theory]
    [InlineData(FeedPath, "Receptionist")]
    [InlineData(FeedPath, "Admin")]
    [InlineData(DailyReportPath, "Admin")]
    [InlineData(MonthlyReportPath, "Admin")]
    public async Task RouteWithAnAllowedRolePassesGatewayAuthorization(string path, string role)
    {
        using var response = await SendAsync(HttpMethod.Get, path, role);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData(FeedPath, "Doctor")]
    [InlineData(DailyReportPath, "Doctor")]
    [InlineData(DailyReportPath, "Receptionist")]
    [InlineData(MonthlyReportPath, "Doctor")]
    [InlineData(MonthlyReportPath, "Receptionist")]
    public async Task RouteWithARoleItDoesNotAllowReturns403(string path, string role)
    {
        using var response = await SendAsync(HttpMethod.Get, path, role);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(FeedPath)]
    [InlineData(DailyReportPath)]
    [InlineData(MonthlyReportPath)]
    public async Task RouteWithoutABearerTokenReturns401(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path, role: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Both endpoints are read-only: the Gateway refuses any other method itself.
    [Theory]
    [InlineData(FeedPath)]
    [InlineData(DailyReportPath)]
    [InlineData(MonthlyReportPath)]
    public async Task RouteDoesNotAcceptWrites(string path)
    {
        using var response = await SendAsync(HttpMethod.Post, path, "Admin");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData("notifications-route", "/api/notifications", "ActivityFeedPolicy")]
    [InlineData("reports-daily-route", "/api/reports/daily", "AdminOnly")]
    [InlineData("reports-monthly-route", "/api/reports/monthly", "AdminOnly")]
    public void RouteIsProxiedToTheNotificationService(string routeId, string path, string policy)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        var configuration = factory.Services.GetRequiredService<IConfiguration>();

        var route = configuration.GetSection($"ReverseProxy:Routes:{routeId}");

        Assert.Equal("notification-cluster", route["ClusterId"]);
        Assert.Equal(policy, route["AuthorizationPolicy"]);
        Assert.Equal(path, route["Match:Path"]);
        var methods = route.GetSection("Match:Methods").Get<string[]>();
        Assert.NotNull(methods);
        Assert.Equal(["GET"], methods);
    }

    [Fact]
    public void NotificationClusterPointsAtTheServicesLocalPort()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        var configuration = factory.Services.GetRequiredService<IConfiguration>();

        Assert.Equal(
            "http://localhost:5005",
            configuration["ReverseProxy:Clusters:notification-cluster:Destinations:notification-destination:Address"]);
    }
}
