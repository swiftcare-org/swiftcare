using System.Security.Claims;
using ApiGateway.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway.UnitTests.Security;

// A missing cluster or an empty destination list is a configuration error. Both validators
// report it as InvalidOperationException, which the middleware turns into a handled 503
// (SWC-151 mutation testing).
public sealed class ValidatorDestinationEdgeCaseTests
{
    private static readonly Guid Doctor = Guid.NewGuid(), Patient = Guid.NewGuid(), Queue = Guid.NewGuid();

    [Fact]
    public async Task ClinicalValidatorWithoutItsClusterFailsWithAConfigurationError()
    {
        var validator = new ClinicalVisitValidator(new HttpClient(new NoCallHandler()), new Proxy(), Configuration());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync(Doctor, Patient, Queue, null, CancellationToken.None));
    }

    [Fact]
    public async Task ClinicalValidatorWithAnEmptyDestinationListNamesTheProblem()
    {
        var validator = new ClinicalVisitValidator(
            new HttpClient(new NoCallHandler()), new Proxy("queue-cluster"), Configuration());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync(Doctor, Patient, Queue, null, CancellationToken.None));

        Assert.Equal("Clinical validation destination is unavailable.", exception.Message);
    }

    [Fact]
    public async Task SessionValidatorWithoutItsClusterFailsWithAConfigurationError()
    {
        var validator = new SessionValidator(new HttpClient(new NoCallHandler()), new Proxy(), Configuration());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync(Principal(), false, CancellationToken.None));
    }

    [Fact]
    public async Task SessionValidatorWithAnEmptyDestinationListNamesTheProblem()
    {
        var validator = new SessionValidator(
            new HttpClient(new NoCallHandler()), new Proxy("auth-cluster"), Configuration());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync(Principal(), false, CancellationToken.None));

        Assert.Equal("Session validation destination is unavailable.", exception.Message);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Gateway:InternalSecret"] = "trusted-secret" })
        .Build();

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity(
    [
        new Claim("sub", Guid.NewGuid().ToString()),
        new Claim("sessionVersion", Guid.NewGuid().ToString()),
        new Claim("jti", "session-id"),
        new Claim("exp", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString())
    ], "Test"));

    private sealed class NoCallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No request may be sent without a destination.");
    }

    // Lists the named clusters, each with an empty destination list.
    private sealed class Proxy(params string[] clusterIds) : IProxyConfigProvider
    {
        public IProxyConfig GetConfig() => new Config(clusterIds);
    }

    private sealed class Config(string[] clusterIds) : IProxyConfig
    {
        public IReadOnlyList<RouteConfig> Routes => [];

        public IReadOnlyList<ClusterConfig> Clusters => clusterIds
            .Select(id => new ClusterConfig { ClusterId = id, Destinations = new Dictionary<string, DestinationConfig>() })
            .ToList();

        public IChangeToken ChangeToken => new CancellationChangeToken(CancellationToken.None);
    }
}
