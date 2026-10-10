using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using ApiGateway.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway.UnitTests.Security;

public sealed class SessionValidatorTests
{
    private static readonly Guid UserId = Guid.NewGuid(), Version = Guid.NewGuid();
    private static ClaimsPrincipal Principal(string? omitted = null, string? changed = null, string changedClaim = "sessionVersion")
    {
        var claims = new List<Claim>();
        foreach (var pair in new Dictionary<string, string>
        {
            ["sub"] = UserId.ToString(),
            ["sessionVersion"] = Version.ToString(),
            ["jti"] = "session-id",
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString()
        })
            if (pair.Key != omitted) claims.Add(new Claim(pair.Key, changed is not null && pair.Key == changedClaim ? changed : pair.Value));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static SessionValidator Create(Handler handler, bool destinationMissing = false) =>
        new(new HttpClient(handler), new Proxy(destinationMissing), new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Gateway:InternalSecret"] = "trusted-secret" }).Build());

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ClaimsAreValidatedAgainstTheAuthDestination(bool revoke, bool valid)
    {
        using var handler = new Handler(valid ? "true" : "false");
        var user = Principal();
        Assert.Equal(valid, await Create(handler).ValidateAsync(user, revoke, CancellationToken.None));
        Assert.Equal("http://auth.test/internal/sessions/validate", handler.Url);
        Assert.Equal("trusted-secret", handler.Secret);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(UserId, body.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal(Version, body.RootElement.GetProperty("sessionVersion").GetGuid());
        Assert.Equal("session-id", body.RootElement.GetProperty("tokenId").GetString());
        Assert.Equal(long.Parse(user.FindFirst("exp")!.Value), body.RootElement.GetProperty("expiresAtUnixSeconds").GetInt64());
        Assert.Equal(revoke, body.RootElement.GetProperty("revoke").GetBoolean());
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("sessionVersion")]
    [InlineData("exp")]
    [InlineData("jti")]
    public async Task MissingClaimsAreRejectedWithoutAnAuthorityCall(string omitted)
    {
        using var handler = new Handler("true");
        Assert.False(await Create(handler).ValidateAsync(Principal(omitted), false, CancellationToken.None));
        Assert.Null(handler.Url);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidSessionVersionIsRejected(string version)
    {
        using var handler = new Handler("true");
        Assert.False(await Create(handler).ValidateAsync(Principal(changed: version), false, CancellationToken.None));
        Assert.Null(handler.Url);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidUserIdentityIsRejectedWithoutAnAuthorityCall(string userId)
    {
        using var handler = new Handler("true");
        Assert.False(await Create(handler).ValidateAsync(Principal(changed: userId, changedClaim: "sub"), false, CancellationToken.None));
        Assert.Null(handler.Url);
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public async Task TokenIdentifiersRespectThePersistenceLengthLimit(int length, bool valid)
    {
        using var handler = new Handler("true");
        Assert.Equal(valid, await Create(handler).ValidateAsync(Principal(changed: new string('x', length), changedClaim: "jti"), false, CancellationToken.None));
        Assert.Equal(valid, handler.Url is not null);
    }

    [Fact]
    public async Task AuthorityFailureCannotBeTreatedAsAValidSession()
    {
        using var handler = new Handler("true", HttpStatusCode.InternalServerError);
        await Assert.ThrowsAsync<HttpRequestException>(() => Create(handler).ValidateAsync(Principal(), true, CancellationToken.None));
    }

    [Fact]
    public async Task InvalidAuthorityPayloadCannotBeTreatedAsAValidSession()
    {
        using var handler = new Handler("invalid");
        await Assert.ThrowsAsync<JsonException>(() => Create(handler).ValidateAsync(Principal(), false, CancellationToken.None));
    }

    [Fact]
    public async Task MissingDestinationFailsClosed()
    {
        using var handler = new Handler("true");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(handler, true).ValidateAsync(Principal(), false, CancellationToken.None));
        Assert.Null(handler.Url);
    }

    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Secret { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Secret = request.Headers.GetValues("X-Gateway-Secret").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(body) };
        }
    }

    private sealed class Proxy(bool destinationMissing) : IProxyConfigProvider
    {
        public IProxyConfig GetConfig() => new Config(destinationMissing);
    }

    private sealed class Config(bool destinationMissing) : IProxyConfig
    {
        public IReadOnlyList<RouteConfig> Routes => [];
        public IReadOnlyList<ClusterConfig> Clusters => [new() { ClusterId = "auth-cluster",
            Destinations = destinationMissing ? null : new Dictionary<string, DestinationConfig> { ["auth"] = new() { Address = "http://auth.test/" } } }];
        public IChangeToken ChangeToken => new CancellationChangeToken(CancellationToken.None);
    }
}
