using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApiGateway.Models;

namespace ApiGateway.UnitTests.Routing;

// Exercises the real appsettings.json ReverseProxy route configuration end to end through
// the ASP.NET Core pipeline, closing the gap left by the pure middleware-unit tests: those
// prove the middleware logic is correct in isolation, but not that the "anonymous"/"default"
// AuthorizationPolicy strings and route Order values in appsettings.json actually produce
// the intended behavior when wired together.
public class ReverseProxyAuthorizationTests
{
    [Fact]
    public async Task LoginRouteDoesNotRequireAuthentication()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        var response = await client.PostAsync("/api/auth/login", new StringContent("{}"));

        // AuthService isn't running in this environment, so a successful proxy attempt
        // fails downstream (e.g. 502) rather than succeeding - what this asserts is only
        // that the Gateway itself did not block the request for lacking a bearer token.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LogoutRouteWithoutABearerTokenIsRejectedWith401BeforeReachingAuthService()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Unauthorized", body!.Message);
    }

    [Fact]
    public async Task LogoutRouteWithAValidBearerTokenPassesGatewayAuthorization()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateSignedToken());

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedRouteWithARevokedTokenIsRejectedWith401()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        var jti = Guid.NewGuid().ToString();
        factory.RevokedTokenStore.Revoke(jti, DateTimeOffset.UtcNow.AddHours(1));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateSignedToken(jti: jti));

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedRouteWithAnExpiredTokenIsRejectedWith401WithAMessageBody()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateSignedToken(expiresAtUtc: DateTime.UtcNow.AddHours(-1)));

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Unauthorized", body!.Message);
    }

    [Fact]
    public async Task HealthCheckDoesNotRequireAuthentication()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
