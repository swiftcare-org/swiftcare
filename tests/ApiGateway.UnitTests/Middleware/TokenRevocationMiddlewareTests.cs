using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApiGateway.Middleware;
using ApiGateway.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiGateway.UnitTests.Middleware;

public class TokenRevocationMiddlewareTests
{
    private static DefaultHttpContext CreateAuthenticatedContext(
        string jti = "test-jti",
        string userId = "11111111-1111-1111-1111-111111111111",
        long? expUnixSeconds = null,
        string method = "GET",
        string path = "/api/auth/logout")
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Jti, jti)
        };

        if (expUnixSeconds is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Exp, expUnixSeconds.Value.ToString()));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
        context.Request.Method = method;
        context.Request.Path = path;

        return context;
    }

    private static TokenRevocationMiddleware CreateMiddleware(RevokedTokenStore store, RequestDelegate next) =>
        new(next, store, NullLogger<TokenRevocationMiddleware>.Instance);

    [Fact]
    public async Task UnauthenticatedRequestSkipsRevocationCheckAndCallsNext()
    {
        var store = new RevokedTokenStore();
        var nextCalled = false;
        var middleware = CreateMiddleware(store, _ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedRequestWithNonRevokedTokenCallsNext()
    {
        var store = new RevokedTokenStore();
        var nextCalled = false;
        var middleware = CreateMiddleware(store, _ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateAuthenticatedContext(method: "GET", path: "/api/patients");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task RevokedTokenIsRejectedWith401AndNeverReachesNext()
    {
        var store = new RevokedTokenStore();
        store.Revoke("already-revoked-jti", DateTimeOffset.UtcNow.AddHours(1));

        var nextCalled = false;
        var middleware = CreateMiddleware(store, _ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateAuthenticatedContext(jti: "already-revoked-jti", method: "GET", path: "/api/patients");

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task RequestWithNoJtiClaimIsRejectedWith401AndNeverReachesNext()
    {
        var store = new RevokedTokenStore();
        var nextCalled = false;
        var middleware = CreateMiddleware(store, _ => { nextCalled = true; return Task.CompletedTask; });

        var identity = new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, "11111111-1111-1111-1111-111111111111")],
            authenticationType: "TestAuth");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task LogoutRequestRevokesTheJtiBeforeForwardingToNext()
    {
        var store = new RevokedTokenStore();
        var jtiWasRevokedBeforeNext = false;
        var middleware = CreateMiddleware(store, _ =>
        {
            jtiWasRevokedBeforeNext = store.IsRevoked("logout-jti");
            return Task.CompletedTask;
        });
        var context = CreateAuthenticatedContext(
            jti: "logout-jti",
            expUnixSeconds: DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            method: "POST",
            path: "/api/auth/logout");

        await middleware.InvokeAsync(context);

        Assert.True(jtiWasRevokedBeforeNext);
        Assert.True(store.IsRevoked("logout-jti"));
    }

    [Fact]
    public async Task NonLogoutRequestDoesNotRevokeTheCallersToken()
    {
        var store = new RevokedTokenStore();
        var middleware = CreateMiddleware(store, _ => Task.CompletedTask);
        var context = CreateAuthenticatedContext(jti: "unrelated-request-jti", method: "GET", path: "/api/patients");

        await middleware.InvokeAsync(context);

        Assert.False(store.IsRevoked("unrelated-request-jti"));
    }

    [Fact]
    public async Task ReplayingTheSameTokenForASecondLogoutIsRejectedRatherThanReRevoked()
    {
        var store = new RevokedTokenStore();
        var nextCallCount = 0;
        var middleware = CreateMiddleware(store, _ => { nextCallCount++; return Task.CompletedTask; });

        var firstContext = CreateAuthenticatedContext(
            jti: "replayed-jti",
            expUnixSeconds: DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            method: "POST",
            path: "/api/auth/logout");
        await middleware.InvokeAsync(firstContext);

        var secondContext = CreateAuthenticatedContext(
            jti: "replayed-jti",
            expUnixSeconds: DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            method: "POST",
            path: "/api/auth/logout");
        await middleware.InvokeAsync(secondContext);

        Assert.Equal(1, nextCallCount);
        Assert.Equal(StatusCodes.Status401Unauthorized, secondContext.Response.StatusCode);
    }
}
