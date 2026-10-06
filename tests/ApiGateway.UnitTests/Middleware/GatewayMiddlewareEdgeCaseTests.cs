using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using ApiGateway.Middleware;
using ApiGateway.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiGateway.UnitTests.Middleware;

// Correlation IDs, forged identity headers and the 401 body of the Gateway middleware
// (SWC-151 mutation testing).
public class GatewayMiddlewareEdgeCaseTests
{
    private const string GatewaySecret = "gateway-secret-value";

    [Fact]
    public async Task CorrelationIdIsGeneratedWhenTheClientSendsNone()
    {
        var context = new DefaultHttpContext();

        await new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance)
            .InvokeAsync(context);

        var generated = context.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.True(Guid.TryParse(generated, out _));
        Assert.Equal(generated, context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task CorrelationIdFromTheClientIsKept()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "corr-123";

        await new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance)
            .InvokeAsync(context);

        Assert.Equal("corr-123", context.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString());
        Assert.Equal("corr-123", context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task BlankCorrelationIdIsReplaced()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "   ";

        await new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance)
            .InvokeAsync(context);

        Assert.True(Guid.TryParse(context.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString(), out _));
    }

    [Fact]
    public async Task CorrelationIdIsAddedToTheLoggingScopeForTheWholeRequest()
    {
        var logger = new ScopeCapturingLogger();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "corr-123";
        var scopeWasOpenDuringNext = false;

        await new CorrelationIdMiddleware(
                _ =>
                {
                    scopeWasOpenDuringNext = logger.OpenScopes == 1;
                    return Task.CompletedTask;
                },
                logger)
            .InvokeAsync(context);

        var scope = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(logger.LastScope);
        Assert.Equal("corr-123", scope["CorrelationId"]);
        Assert.True(scopeWasOpenDuringNext);
        Assert.Equal(0, logger.OpenScopes);
    }

    [Theory]
    [InlineData("X-User-Name")]
    [InlineData("X-Room-Number")]
    public async Task AnonymousRequestCannotForwardForgedNameOrRoom(string header)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[header] = "forged";

        await new GatewayForwardingMiddleware(_ => Task.CompletedTask, Configuration()).InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey(header));
    }

    [Fact]
    public async Task DoctorNameIsForwardedFromTheFullNameClaim()
    {
        var context = AuthenticatedContext(new Claim("fullName", "Dr. Amara Chen"));
        context.Request.Headers["X-User-Name"] = "forged";

        await new GatewayForwardingMiddleware(_ => Task.CompletedTask, Configuration()).InvokeAsync(context);

        Assert.Equal("Dr. Amara Chen", context.Request.Headers["X-User-Name"].ToString());
    }

    [Fact]
    public async Task NameHeaderIsOmittedWhenTheTokenHasNoFullNameClaim()
    {
        var context = AuthenticatedContext();

        await new GatewayForwardingMiddleware(_ => Task.CompletedTask, Configuration()).InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey("X-User-Name"));
    }

    [Fact]
    public void MissingGatewaySecretNamesTheEnvironmentVariableToSet()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new GatewayForwardingMiddleware(_ => Task.CompletedTask, new ConfigurationBuilder().Build()));

        Assert.Equal(
            "Gateway:InternalSecret is not configured. Set it via the Gateway__InternalSecret environment variable.",
            exception.Message);
    }

    [Fact]
    public async Task RevokedTokenGetsTheStandardUnauthorizedBody()
    {
        var store = new RevokedTokenStore();
        store.Revoke("revoked-jti", DateTimeOffset.UtcNow.AddHours(1));
        var context = AuthenticatedContext(new Claim(JwtRegisteredClaimNames.Jti, "revoked-jti"));
        context.Request.Path = "/api/patients";
        context.Response.Body = new MemoryStream();

        await new TokenRevocationMiddleware(_ => Task.CompletedTask, store, NullLogger<TokenRevocationMiddleware>.Instance)
            .InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("Unauthorized", body.RootElement.GetProperty("message").GetString());
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Gateway:InternalSecret"] = GatewaySecret })
        .Build();

    private static DefaultHttpContext AuthenticatedContext(params Claim[] extraClaims)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "11111111-1111-1111-1111-111111111111"),
            new("role", "Doctor")
        };
        claims.AddRange(extraClaims);
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth"))
        };
    }

    // Records the state passed to BeginScope so the test can check what every log line
    // written during the request is tagged with.
    private sealed class ScopeCapturingLogger : ILogger<CorrelationIdMiddleware>
    {
        public object? LastScope { get; private set; }

        public int OpenScopes { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            LastScope = state;
            OpenScopes++;
            return new Scope(this);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class Scope(ScopeCapturingLogger owner) : IDisposable
        {
            public void Dispose() => owner.OpenScopes--;
        }
    }
}
