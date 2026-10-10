using System.Security.Claims;
using ApiGateway.Middleware;
using ApiGateway.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiGateway.UnitTests.Middleware;

public sealed class SessionValidationTests
{
    private static DefaultHttpContext Context(string method = "GET", string path = "/api/patients")
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([], "Test")) };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Theory]
    [InlineData("POST", "/api/auth/logout", true)]
    [InlineData("POST", "/API/AUTH/LOGOUT/", true)]
    [InlineData("GET", "/api/auth/logout", false)]
    [InlineData("POST", "/api/patients", false)]
    public async Task DurableValidationPrecedesForwardingAndLogoutIsPersisted(string method, string path, bool revoke)
    {
        var context = Context(method, path);
        var sessions = new Validator();
        var forwarded = false;
        await new SessionValidationMiddleware(ctx =>
        {
            forwarded = true;
            Assert.Equal(1, sessions.Calls);
            Assert.Same(ctx.User, sessions.User);
            return Task.CompletedTask;
        }, NullLogger<SessionValidationMiddleware>.Instance).InvokeAsync(context, sessions);
        Assert.True(forwarded);
        Assert.Equal(revoke, sessions.Revoke);
        Assert.Equal(context.RequestAborted, sessions.Token);
    }

    [Fact]
    public async Task AnonymousRequestsDoNotConsultSessionState()
    {
        var context = new DefaultHttpContext();
        var sessions = new Validator { Failure = new InvalidOperationException() };
        var forwarded = false;
        await new SessionValidationMiddleware(_ => { forwarded = true; return Task.CompletedTask; },
            NullLogger<SessionValidationMiddleware>.Instance).InvokeAsync(context, sessions);
        Assert.True(forwarded);
        Assert.Equal(0, sessions.Calls);
    }

    [Fact]
    public async Task InvalidSessionNeverReachesTheBackend()
    {
        var context = Context();
        await RejectAsync(context, new Validator { Valid = false }, 401, "Unauthorized");
    }

    [Theory]
    [InlineData("http")]
    [InlineData("json")]
    [InlineData("configuration")]
    [InlineData("timeout")]
    public async Task ValidationOutageFailsClosedWithoutAcknowledgingLogout(string failure)
    {
        Exception error = failure switch
        {
            "http" => new HttpRequestException(),
            "json" => new System.Text.Json.JsonException(),
            "configuration" => new InvalidOperationException(),
            _ => new OperationCanceledException()
        };
        await RejectAsync(Context("POST", "/api/auth/logout"), new Validator { Failure = error }, 503, "temporarily unavailable");
    }

    [Fact]
    public async Task ClientCancellationIsNotAnOutage()
    {
        var context = Context();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        context.RequestAborted = cancellation.Token;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SessionValidationMiddleware(_ => throw new InvalidOperationException("Must not forward"),
                NullLogger<SessionValidationMiddleware>.Instance).InvokeAsync(context, new Validator { Failure = new OperationCanceledException() }));
    }

    private static async Task RejectAsync(DefaultHttpContext context, Validator sessions, int status, string text)
    {
        var forwarded = false;
        await new SessionValidationMiddleware(_ => { forwarded = true; return Task.CompletedTask; },
            NullLogger<SessionValidationMiddleware>.Instance).InvokeAsync(context, sessions);
        Assert.False(forwarded);
        Assert.Equal(status, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        Assert.Contains(text, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    private sealed class Validator : ISessionValidator
    {
        public bool Valid { get; init; } = true;
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public ClaimsPrincipal? User { get; private set; }
        public bool Revoke { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<bool> ValidateAsync(ClaimsPrincipal user, bool revoke, CancellationToken cancellationToken)
        {
            Calls++; User = user; Revoke = revoke; Token = cancellationToken;
            return Failure is null ? Task.FromResult(Valid) : Task.FromException<bool>(Failure);
        }
    }
}
