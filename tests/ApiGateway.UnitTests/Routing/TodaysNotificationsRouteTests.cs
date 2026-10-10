using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway.UnitTests.Routing;

public sealed class TodaysNotificationsRouteTests
{
    [Theory]
    [InlineData("Receptionist", true)]
    [InlineData("Admin", true)]
    [InlineData("Doctor", false)]
    public async Task TodayRouteUsesTheExistingFeedDestinationAndRolePolicy(string role, bool permitted)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        var route = Assert.Single(factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig().Routes,
            route => route.Match.Path == "/api/notifications/today");
        Assert.Equal("notification-cluster", route.ClusterId);
        Assert.Equal("ActivityFeedPolicy", route.AuthorizationPolicy);
        Assert.Equal(new[] { "GET" }, route.Match.Methods);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"));
        var result = await factory.Services.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(user, null, route.AuthorizationPolicy!);
        Assert.Equal(permitted, result.Succeeded);
    }

    [Fact]
    public async Task AnonymousRequestsAreRejectedBeforeForwarding()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/notifications/today");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
