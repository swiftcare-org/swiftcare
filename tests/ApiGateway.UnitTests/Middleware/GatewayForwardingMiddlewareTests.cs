using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApiGateway.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ApiGateway.UnitTests.Middleware;

public class GatewayForwardingMiddlewareTests
{
    private const string GatewaySecret = "test-gateway-internal-secret";

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Gateway:InternalSecret"] = GatewaySecret })
            .Build();

    private static GatewayForwardingMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, CreateConfiguration());

    [Fact]
    public void ConstructorThrowsWhenGatewayInternalSecretIsNotConfigured()
    {
        var emptyConfiguration = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => new GatewayForwardingMiddleware(_ => Task.CompletedTask, emptyConfiguration));
    }

    [Fact]
    public async Task GatewaySecretHeaderIsAlwaysAttachedRegardlessOfAuthenticationState()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(GatewaySecret, context.Request.Headers["X-Gateway-Secret"]);
    }

    [Fact]
    public async Task ClientSuppliedIdentityHeadersAreStrippedAndReplacedWithClaimValues()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Id"] = "forged-user-id";
        context.Request.Headers["X-User-Role"] = "forged-role";
        context.Request.Headers["X-Gateway-Secret"] = "forged-secret";

        var identity = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, "real-user-id"),
                new Claim("role", "Doctor"),
                new Claim("fullName", "Dr. Amara Chen")
            ],
            authenticationType: "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        await middleware.InvokeAsync(context);

        Assert.Equal("real-user-id", context.Request.Headers["X-User-Id"]);
        Assert.Equal("Doctor", context.Request.Headers["X-User-Role"]);
        Assert.Equal(GatewaySecret, context.Request.Headers["X-Gateway-Secret"]);
    }

    [Fact]
    public async Task RoomNumberHeaderOmittedWhenClaimAbsent()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();

        var identity = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, "receptionist-user-id"),
                new Claim("role", "Receptionist"),
                new Claim("fullName", "Priya Fernando")
            ],
            authenticationType: "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey("X-Room-Number"));
    }

    [Fact]
    public async Task RoomNumberHeaderPresentWhenClaimPresent()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();

        var identity = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, "doctor-user-id"),
                new Claim("role", "Doctor"),
                new Claim("fullName", "Dr. Amara Chen"),
                new Claim("roomNumber", "R-204")
            ],
            authenticationType: "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        await middleware.InvokeAsync(context);

        Assert.Equal("R-204", context.Request.Headers["X-Room-Number"]);
    }

    [Fact]
    public async Task AnonymousRequestReceivesNoIdentityHeaders()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey("X-User-Id"));
        Assert.False(context.Request.Headers.ContainsKey("X-User-Role"));
        Assert.False(context.Request.Headers.ContainsKey("X-User-Name"));
        Assert.False(context.Request.Headers.ContainsKey("X-Room-Number"));
    }

    [Fact]
    public async Task AnonymousRequestWithForgedIdentityHeadersHasThemStrippedNotForwarded()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Id"] = "forged-user-id";
        context.Request.Headers["X-User-Role"] = "forged-role";

        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey("X-User-Id"));
        Assert.False(context.Request.Headers.ContainsKey("X-User-Role"));
    }

    [Fact]
    public async Task NextDelegateIsAlwaysInvoked()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
